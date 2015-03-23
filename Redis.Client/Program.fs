let writePrompt (host:string, port) =
    printf "%s:%d> " host port

let AutoAuth(redis : Redis.Client.Net.RedisConnection, password : string, host, port) =
    writePrompt(host,port)
    System.Console.WriteLine "AUTH ***********"
    redis.SendCommands ("AUTH", password)

let parse (line:string) = 
    line.Split(' ')

let rec readCommand (redis : Redis.Client.Net.RedisConnection, host, port) = 
    writePrompt(host,port)
    let command = System.Console.ReadLine()

    if (System.String.IsNullOrEmpty(command)) then
        ()
    elif redis.SendCommands(parse(command)) = false then
        System.Console.WriteLine("Error: could not send the command")
    elif command.ToUpper() = "QUIT" then
        ()
       
    readCommand (redis, host, port) |> ignore   

[<EntryPoint>]
let main argv = 
    try
        let host = "acasquete.redis.cache.windows.net"
        let port = 6380
        let password = "yE4GhOiUtmUZX1ly46rTFnNGPfFuDLfjlOdkRhph/Is="
        let useSsl = port <> 6379
        
        let redis = new Redis.Client.Net.RedisConnection(host, port, 30, useSsl)
        redis.Connect
        redis.MessageReceived.Add(fun (args) -> 
            let foreGroundColor = System.Console.ForegroundColor;
            let messageColor = 
                match args.Message with
                | x when x.StartsWith("-ERR") -> System.ConsoleColor.Red
                | x when x.StartsWith("(nil)") -> System.ConsoleColor.Blue
                | x when x.StartsWith("+") -> System.ConsoleColor.Green
                | _ -> System.ConsoleColor.DarkGray

            System.Console.ForegroundColor <- messageColor
            System.Console.WriteLine(args.Message);
            System.Console.ForegroundColor <- foreGroundColor;
        )
            
        AutoAuth(redis, password, host, port) |> ignore
        readCommand (redis, host, port)
        //redis.MessageReceived -= OnMessageReceived;
        0

    with 
        | :? System.Exception as ex -> printfn "%s" ex.Message 
                                       0

