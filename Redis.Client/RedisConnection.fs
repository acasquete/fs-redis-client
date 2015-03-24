namespace Redis.Client.Net
    open System
    open System.Globalization
    open System.IO
    open System.Net.Security
    open System.Net.Sockets
    open System.Text
    open System.Threading
    open Redis.Client.Net.Common

    type RedisMessageReceiveEventArgs<'a>(message : string) =
        inherit System.EventArgs()
        member x.Message = message

    type RedisMessageDelegate<'a> = 
        delegate of obj * RedisMessageReceiveEventArgs<'a> -> unit

    type RedisConnection(host, port, timeout, useSsl, alias) = 
        let BufferSize = 16 * 1024
        let socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        let mutable socketStream : Stream = null
        let Host  : string = host
        let Port  : int = port
        let Alias : string = alias
        let Timeout = timeout
        let useSsl = useSsl

        let connectingEventArgsEvent = new Event<_>()
        let disconnectingEventArgsEvent = new Event<_>()
        let customEventHandlerEvent = new Event<RedisMessageDelegate<string>, RedisMessageReceiveEventArgs<string>>()

        member x.ParseLine (line, tabs) =
            let ParseNext() = 
                x.ParseLine(x.ReadLine(), tabs + 1)

            let NestedArray size =
                [| for i in 1..size -> sprintf "%s%s%d) %s" (if i>1 then "\n" else "") (String.replicate tabs " ") i (ParseNext()) |]

            let NestedString size =
                let sb = new StringBuilder()
                while sb.Length <= size do
                    sb.AppendLine(string <| x.ReadLine()) |> ignore
                let a = sb.ToString()         
                a.TrimEnd(char "\r", char "\n")

            match line with
            | Prefix "*0" _   -> "(empty list or set)"
            | Prefix "$-1" _  -> "(nil)"
            | Prefix "*" rest -> rest |> int |> NestedArray |> Array.reduce (+) |> sprintf "%s"
            | Prefix "$" rest -> rest |> int |> NestedString |> sprintf "\"%s\""
            | Prefix ":" rest -> sprintf "(integer) %s" rest
            | Prefix "-" rest when tabs = 1 -> sprintf "(error) %s" rest
            | Prefix "+" rest when tabs > 1 -> rest
            | line -> sprintf "%s" line

        member x.WritePrompt =
            ((if hasContent Alias then Alias else Host), Port) ||> printf "%s:%d> " 

        member x.StartRead() = 
            let message = x.ParseLine (x.ReadLine(), 1)
            x.OnMessageReceive(message)

        member x.ReadLine() =
            let toC (o:obj) = Convert.ToChar(o)
            let n = ref 0
            let sb = new StringBuilder()        
            while !n <> 10 do
               n := socketStream.ReadByte()
               if !n <> 13 && !n <> 10 then sb.Append(toC !n) |> ignore
            sb.ToString()
               
        member x.Connect password = 
            socket.NoDelay <- true
            socket.ReceiveBufferSize <- 16000
            socket.SendTimeout <- Timeout
            socket.Connect(Host, Port)

            if socket.Connected = false then
                socket.Close()

            socketStream <- new NetworkStream(socket)
                
            if useSsl then
                let sslStream = new SslStream(socketStream, false, null, null)
                sslStream.AuthenticateAsClient(Host)

                if sslStream.IsEncrypted = false then
                    raise <| System.Exception("Could not establish an encrypted connection to " + Host)

                socketStream <- sslStream

            x.OnConnecting

            if hasContent password then x.AutoAuth password
        
        member x.SendCommands(args : string[]) =
            let SendBuffer(command:string) =
                let bytes = Encoding.UTF8.GetBytes(command)  
                socketStream.Write(bytes, 0, bytes.Length)

            let sb = new StringBuilder()
            sb.Append("*" + args.Length.ToString() + "\r\n") |> ignore
            args 
            |> Array.iter (fun item -> sb.Append("$" + item.Length.ToString() + "\r\n") |> ignore
                                       sb.Append(item.ToString() + "\r\n") |> ignore)

            SendBuffer(sb.ToString())
            x.StartRead()

        member x.AutoAuth password =
            x.WritePrompt
            printfn "AUTH ********"
            [|"AUTH"; password|] |> x.SendCommands

        [<CLIEvent>]
        member x.Connecting = connectingEventArgsEvent.Publish
       
        [<CLIEvent>]
        member x.Disconnecting = disconnectingEventArgsEvent.Publish

        [<CLIEvent>]
        member x.MessageReceived = customEventHandlerEvent.Publish
 
        member x.OnConnecting =
            connectingEventArgsEvent.Trigger()

        member x.OnDisconnecting =
            disconnectingEventArgsEvent.Trigger()

        member x.OnMessageReceive message =
            customEventHandlerEvent.Trigger(x, new RedisMessageReceiveEventArgs<_>(message))

        member x.Dispose(disposing) = 
             if (disposing) then
                x.OnDisconnecting
                socket.Close()
                socketStream.Dispose()
        
        interface System.IDisposable with 
            member x.Dispose() = 
                x.Dispose(true)
                GC.SuppressFinalize(x)