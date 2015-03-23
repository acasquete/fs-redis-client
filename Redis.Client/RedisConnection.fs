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
 
        member this.Message = message

    type MyCustomDelegate<'a> = delegate of obj * RedisMessageReceiveEventArgs<'a> -> unit

    type RedisConnection(host, port, timeout, useSsl) = 
        let BufferSize = 16 * 1024
        let EndData = [| byte('\r'); byte('\n') |]
        let outBuffer = new ByteBuffer()
        let inBuffer = new ByteBuffer()

        let mutable socket : Socket = null
        let mutable socketStream : Stream = null
        let mutable indentCount = 0

        let Host : string = host
        let Port : int = port
        let Timeout = timeout
        let useSsl = useSsl

        let connectingEventArgsEvent = new Event<_>()
        let disconnectingEventArgsEvent = new Event<_>()
        let customEventHandlerEvent = new Event<MyCustomDelegate<string>, RedisMessageReceiveEventArgs<string>>()
 
//        override this.Dispose(disposing) = 
//             if (disposing) then
//                this.OnDisconnecting();
//                this.socket.Close();
//                this.outBuffer.Dispose();
//                this.socket = null;
//                this.socketStream.Dispose();
//                this.socketStream = null;
//        
//        interface System.IDisposable with 
//            member this.Dispose() = 
//                this.Dispose(true);
//                GC.SuppressFinalize(this);

        let rec ParseLine (line:string) =
            let sb = new StringBuilder()
            if line.StartsWith("*") then
                let size = Int32.Parse(line.Substring(1))
                sb.AppendLine ("Array[" + size.ToString() + "]") |> ignore
                for i in 1 .. size do
                    indentCount <- indentCount + 1
                    sb.AppendLine(new String(' ', indentCount * 2) + (i + 1).ToString() + ") " + ParseLine(inBuffer.ReadString())) |> ignore
                    indentCount <- indentCount - 1
            elif (line.StartsWith("$-1")) then
                sb.Append ("(nil)") |> ignore
            elif (line.StartsWith("$")) then
                sb.Append(ParseLine(inBuffer.ReadString())) |> ignore
            elif (line.StartsWith(":")) then
                sb.Append(line.Substring(1)) |> ignore
            else
                sb.Append(line) |> ignore

            sb.ToString()

        
        member this.EndReceive(ar : IAsyncResult) = 
            if socketStream = null then
                printf "aaa"

            let buffer = ar.AsyncState :?> byte[]
            
            try
                let read = socketStream.EndRead(ar)
                inBuffer.Write(buffer, 0, read)

                if read < buffer.Length then
                    if inBuffer.Length > 0 then
                        inBuffer.StartRead()
                        let message = ParseLine(inBuffer.ReadString())
                        this.OnMessageReceive(message)
                        inBuffer.Clear()
                else
                    this.BeginReceive
            with 
            | :? IOException -> printf "a" 

            this.BeginReceive

        member this.BeginReceive =
            let buffer: byte [] = Array.zeroCreate BufferSize
        
            if socketStream <> null then
                socketStream.BeginRead(buffer, 0, buffer.Length, (fun ar -> this.EndReceive ar), buffer) |> ignore
                
        member this.Connect = 
            if socket = null then
                socket <- new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                socket.NoDelay <- true
                socket.SendTimeout <- Timeout
                socket.Connect(Host, Port)

                if socket.Connected = false then
                    socket.Close()
                    socket <- null

                socketStream <- new NetworkStream(socket);
                
                if useSsl then
                    let sslStream = new SslStream(socketStream, false, null, null)
                    sslStream.AuthenticateAsClient(Host)

                    if (sslStream.IsEncrypted = false) then
                        raise (System.Exception("Could not establish an encrypted connection to " + Host))

                    socketStream <- sslStream;

                this.OnConnecting
                this.BeginReceive
            else
                printf "a"


        member this.SendBuffer =
            if socket = null then
                this.Connect

            if socket = null then
                ()

            try
                outBuffer.StartRead()
                let bytes: byte [] = Array.zeroCreate BufferSize
                let bytesRead = outBuffer.Read(bytes, 0, bytes.Length)
                socketStream.Write(bytes, 0, bytesRead)
            with 
            | :? SocketException -> 
                    socket.Close()
                    socket <- null
            true
        
        member this.SendCommand([<ParamArray>] args : byte[][]) =
            //if (this.socket == null)
            this.Connect
            
            outBuffer.Write(System.Text.Encoding.ASCII.GetBytes("*" + args.Length.ToString()))
            outBuffer.Write(EndData)
           
            for arg in args do
                outBuffer.Write(System.Text.Encoding.ASCII.GetBytes("$" + arg.Length.ToString()))
                outBuffer.Write(EndData)
                outBuffer.Write(arg)
                outBuffer.Write(EndData)

            this.SendBuffer

        member this.SendCommands([<ParamArray>] args:string[]) =
            this.SendCommand(args.ToByteArrays())

        member this.Connected with get () = if socket <> null then socket.Connected else false

        [<CLIEvent>]
        member this.Connecting = connectingEventArgsEvent.Publish
       
        [<CLIEvent>]
        member this.Disconnecting = disconnectingEventArgsEvent.Publish

        [<CLIEvent>]
        member this.MessageReceived = customEventHandlerEvent.Publish
 
        member this.OnConnecting =
            connectingEventArgsEvent.Trigger()

        member this.OnDisconnecting =
            disconnectingEventArgsEvent.Trigger()

        member this.OnMessageReceive x =
            customEventHandlerEvent.Trigger(this, new RedisMessageReceiveEventArgs<_>(x))
