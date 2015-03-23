namespace Redis.Client.Net
    
    open System
    open System.Globalization
    open System.IO
    open System.Net.Security
    open System.Net.Sockets
    open System.Text
    open System.Threading
    open Redis.Client.Utilities

    type RedisMessageReceiveEventArgs<'a>(message : string) =
        inherit System.EventArgs()
        member x.Message = message

    type MyCustomDelegate<'a> = 
        delegate of obj * RedisMessageReceiveEventArgs<'a> -> unit

    type RedisConnection(host, port, timeout, useSsl) = 
        let BufferSize = 16 * 1024
        let EndData = [| byte('\r'); byte('\n') |]
        let outBuffer = new ByteBuffer()
        let inBuffer = new ByteBuffer()

        let mutable socket : Socket = null
        let mutable socketStream : Stream = null

        let Host : string = host
        let Port : int = port
        let Timeout = timeout
        let useSsl = useSsl

        let connectingEventArgsEvent = new Event<_>()
        let disconnectingEventArgsEvent = new Event<_>()
        let customEventHandlerEvent = new Event<MyCustomDelegate<string>, RedisMessageReceiveEventArgs<string>>()

        let (|Prefix|_|) (p:string) (s:string) =
            if s.StartsWith(p) then Some(s.Substring(p.Length)) else None

        let rec ParseLine (line, tabs) =
            let parseNext() = 
                ParseLine(inBuffer.ReadString(), tabs + 1)

            let nestedArray size =
                [| for i in 1..size -> sprintf "%s%d) %s\n" (String.replicate tabs " ") i (parseNext()) |]

            match line with
            | Prefix "*" rest -> rest |> int |> nestedArray |> Array.reduce (+) |> sprintf "%s"
            | Prefix "$-1" rest -> "(nil)"
            | Prefix "$" rest -> parseNext()
            | Prefix ":" rest -> rest
            | l -> l

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
                 
        member x.Connect = 
            if socket = null then
                socket <- new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                socket.NoDelay <- true
                socket.SendTimeout <- Timeout
                socket.Connect(Host, Port)

                if socket.Connected = false then
                    socket.Close()

                socketStream <- new NetworkStream(socket);
                
                if useSsl then
                    let sslStream = new SslStream(socketStream, false, null, null)
                    sslStream.AuthenticateAsClient(Host)

                    if (sslStream.IsEncrypted = false) then
                        raise (System.Exception("Could not establish an encrypted connection to " + Host))

                    socketStream <- sslStream;

                x.OnConnecting

        member x.SendBuffer =
            try
                outBuffer.StartRead()
                let bytes: byte [] = Array.zeroCreate BufferSize
                let bytesRead = outBuffer.Read(bytes, 0, bytes.Length)
                socketStream.Write(bytes, 0, bytesRead)
                outBuffer.Clear()
                x.StartRead
            with 
            | :? SocketException -> 
                    socket.Close()
            true
        
        member x.SendCommand([<ParamArray>] args : byte[][]) =
            outBuffer.Write(System.Text.Encoding.ASCII.GetBytes("*" + args.Length.ToString()))
            outBuffer.Write(EndData)
           
            for arg in args do
                outBuffer.Write(System.Text.Encoding.ASCII.GetBytes("$" + arg.Length.ToString()))
                outBuffer.Write(EndData)
                outBuffer.Write(arg)
                outBuffer.Write(EndData)

            x.SendBuffer

        member x.SendCommands([<ParamArray>] args:string[]) =
            x.SendCommand(args.ToByteArrays())

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
                x.Dispose(true);
                GC.SuppressFinalize(x);