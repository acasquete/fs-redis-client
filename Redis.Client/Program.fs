let writePrompt() =
    System.Console.Write "> "

let AutoAuth(redis : Redis.Client.Net.RedisConnection, password : string) =
    writePrompt()
    System.Console.WriteLine "AUTH ***********"
    redis.SendCommands ("AUTH", password)

let parse (line:string) = 
    line.Split(' ')

let rec readCommand (redis : Redis.Client.Net.RedisConnection) = 
    writePrompt()
    let command = System.Console.ReadLine()

    if (System.String.IsNullOrEmpty(command)) then
        ()
    elif redis.SendCommands(parse(command)) = false then
        System.Console.WriteLine("Error: could not send the command")
    elif command.ToUpper() = "QUIT" then
        ()
       
    readCommand redis |> ignore   

[<EntryPoint>]
let main argv = 
    try
        let host = "acasquete.redis.cache.windows.net"
        let port = 6380
        let password = ""
        let useSsl = port <> 6379
        
        let redis = new Redis.Client.Net.RedisConnection(host, port, 30, useSsl)

        redis.MessageReceived.Add(fun (args) -> 
            let rewritePrompt = System.Console.CursorLeft > 0;
            System.Console.SetCursorPosition(0, System.Console.CursorTop);

            let color = System.Console.ForegroundColor;

            if (args.Message.StartsWith("-")) then System.Console.ForegroundColor <- System.ConsoleColor.Red
            elif (args.Message.StartsWith("(nil)")) then System.Console.ForegroundColor <- System.ConsoleColor.Blue
            elif (args.Message.StartsWith("+")) then System.Console.ForegroundColor <- System.ConsoleColor.Green
            else System.Console.ForegroundColor <- System.ConsoleColor.DarkGray;

            System.Console.WriteLine(args.Message);

            System.Console.ForegroundColor <- color;
            if rewritePrompt then
                writePrompt())
            
        AutoAuth(redis, password) |> ignore
        readCommand (redis)
        //redis.MessageReceived -= OnMessageReceived;
        0

    with 
        | :? System.Exception as ex -> printfn "%s" ex.Message 
                                       0

