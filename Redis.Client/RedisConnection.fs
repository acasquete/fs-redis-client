namespace Redis.Client.Net
    open System
    open System.Globalization
    open System.IO
    open System.Net.Security
    open System.Net.Sockets
    open System.Text
    open System.Threading
    open Redis.Client.Utilities
    open Redis.Client.Net.Common

    type RedisMessageReceiveEventArgs<'a>(message : string) =
        inherit System.EventArgs()
        member x.Message = message

    type RedisMessageDelegate<'a> = 
        delegate of obj * RedisMessageReceiveEventArgs<'a> -> unit

    type RedisConnection(host, port, timeout, useSsl, alias) = 
        let BufferSize = 16 * 1024
        let EndData = [| byte('\r'); byte('\n') |]
        let outBuffer = new ByteBuffer()
        let inBuffer = new ByteBuffer()
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

        let rec ParseLine (line, tabs) =
            let ParseNext() = 
                ParseLine(inBuffer.ReadString(), tabs + 1)

            let NestedArray size =
                [| for i in 1..size -> sprintf "%s%s%d) %s" (if i>1 then "\n" else "") (String.replicate tabs " ") i (ParseNext()) |]

            match line with
            | "" -> ""
            | Prefix "*0" _ -> "(empty list or set)"
            | Prefix "*" rest -> rest |> int |> NestedArray |> Array.reduce (+) |> sprintf "%s"
            | Prefix "$-1" _ -> "(nil)"
            | Prefix "$" _ -> sprintf @"""%s""" (ParseNext())
            | Prefix ":" rest -> sprintf "(integer) %s" rest
            | Prefix "-" rest when tabs = 1 -> sprintf "(error) %s" rest
            | Prefix "+" rest when tabs > 1 -> rest
            | line -> sprintf "%s" line

        member x.WritePrompt =
            ((if hasContent Alias then Alias else Host), Port) ||> printf "%s:%d> " 

        member x.StartRead =
            let buffer: byte [] = Array.zeroCreate BufferSize
            let read = socketStream.Read(buffer, 0, buffer.Length)
            inBuffer.Write(buffer, 0, read)

            if read < buffer.Length then
                if inBuffer.Length > 0 then
                    inBuffer.StartRead()
                    let s = inBuffer.ReadString()
                    let message = ParseLine(s, 1)
                    x.OnMessageReceive(message)
                    inBuffer.Clear()
            else 
                x.StartRead
                 
        member x.Connect password = 
            socket.NoDelay <- true
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
        
        member x.SendCommand(args : byte[][]) =
            let SendBuffer() =
                try
                    outBuffer.StartRead()
                    let bytes = Array.zeroCreate BufferSize
                    let bytesRead = outBuffer.Read(bytes, 0, bytes.Length)
                    socketStream.Write(bytes, 0, bytesRead)
                    outBuffer.Clear()
                    x.StartRead
                with 
                | :? SocketException -> socket.Close()

            outBuffer.Write(Text.Encoding.ASCII.GetBytes("*" + args.Length.ToString()))
            outBuffer.Write(EndData)
           
            for arg in args do
                outBuffer.Write(Text.Encoding.ASCII.GetBytes("$" + arg.Length.ToString()))
                outBuffer.Write(EndData)
                outBuffer.Write(arg)
                outBuffer.Write(EndData)

            SendBuffer()

        member x.SendCommands(args : string[]) =
            args |> Array.map (fun x -> Text.Encoding.ASCII.GetBytes x) |> x.SendCommand

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
                outBuffer.Dispose()
                socketStream.Dispose()
        
        interface System.IDisposable with 
            member x.Dispose() = 
                x.Dispose(true)
                GC.SuppressFinalize(x)