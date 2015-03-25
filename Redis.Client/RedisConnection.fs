namespace Redis.Client.Net
    open System
    open System.Globalization
    open System.IO
    open System.Net.Security
    open System.Net.Sockets
    open System.Text
    open System.Threading
    open Redis.Client.Net.Common

    type RedisConnection(host, port, timeout, useSsl, alias) = 
        let socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        let mutable socketStream : Stream = null
        let Host  : string = host
        let Port  : int = port
        let Alias : string = alias
        let Timeout = timeout
        let useSsl  = useSsl
        let messageReceivedEvent = new Event<string>()

        let OnMessageReceive message = messageReceivedEvent.Trigger(message)

        let readLine() =
            let n = ref 0
            let sb = new StringBuilder()        
            while !n <> 10 do
               n := socketStream.ReadByte()
               if !n <> 13 && !n <> 10 then sb.Append(char !n) |> ignore
            sb.ToString()

        let rec parseLine (line, tabs) =
            let parseNext() = 
                parseLine(readLine(), tabs + 1)

            let NestedArray size = // TODO: Fix tabs (nested arrays??)
                [| for i in 1..size -> sprintf "%s%s%d) %s" (if i>1 then "\n" else "") (String.replicate tabs " ") i (parseNext()) |]

            let NestedString size =
                let sb = new StringBuilder()
                while sb.Length <= size do sb.AppendLine(string <| readLine()) |> ignore
                sb.ToString().TrimEnd(char "\r", char "\n")        

            match line with
            | Prefix "*0" _   -> "(empty list or set)"
            | Prefix "$-1" _  -> "(nil)"
            | Prefix "*" rest -> rest |> int |> NestedArray |> Array.reduce (+) |> sprintf "%s"
            | Prefix "$" rest -> rest |> int |> NestedString |> sprintf "\"%s\""
            | Prefix ":" rest -> sprintf "(integer) %s" rest
            | Prefix "-" rest when tabs = 1 -> sprintf "(error) %s" rest
            | Prefix "+" rest when tabs > 1 -> rest
            | line            -> sprintf "%s" line

        let startRead() = 
            let message = parseLine (readLine(), 1)
            OnMessageReceive(message)

        let sendCommands(args : string[]) =
            let sb = new StringBuilder()
            sb.Append("*" + string args.Length + "\r\n") |> ignore
            args 
            |> Array.iter (fun item -> sb.Append("$" + string item.Length + "\r\n") |> ignore
                                       sb.Append(item + "\r\n") |> ignore)
            
            let bytes = sb |> string |> Encoding.UTF8.GetBytes 
            socketStream.Write(bytes, 0, bytes.Length)
            startRead()
 
        let writePrompt() =
            ((if hasContent Alias then Alias else Host), Port) ||> printf "%s:%d> "
 
        let authenticateWith password =
            writePrompt()
            printfn "AUTH ********"
            [|"AUTH"; password|] |> sendCommands

        let connect password =
            socket.NoDelay <- true
            socket.ReceiveBufferSize <- 16000
            socket.SendTimeout <- Timeout
            socket.Connect(Host, Port)

            if socket.Connected = false then socket.Close()

            socketStream <- new NetworkStream(socket)
                
            if useSsl then
                let sslStream = new SslStream(socketStream, false, null, null)
                sslStream.AuthenticateAsClient(Host)

                if sslStream.IsEncrypted = false then
                    raise <| System.Exception("Could not establish an encrypted connection to " + Host)

                socketStream <- sslStream

            if password |> hasContent then authenticateWith password

        member x.Connect = connect

        member x.WritePrompt() = writePrompt()

        member x.SendCommands = sendCommands

        [<CLIEvent>]
        member x.MessageReceived = messageReceivedEvent.Publish

        member x.Dispose(disposing) = 
             if (disposing) then
                socket.Close()
                socketStream.Dispose()
        
        interface System.IDisposable with 
            member x.Dispose() = 
                x.Dispose(true)
                GC.SuppressFinalize(x)