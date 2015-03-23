open System
open Redis.Client.Net

let writePrompt (host:string, port) =
    printf "%s:%d> " host port

let AutoAuth(redis : RedisConnection, password : string, host, port) =
    writePrompt(host,port)
    Console.WriteLine "AUTH ***********"
    redis.SendCommands ("AUTH", password)

let parse (line:string) = 
    line.Split(' ')

let parseResponse (line:string) = 
    match line with
    | x when x.StartsWith("+") -> x.Substring(1)
    | _ -> line

let rec readCommand (redis : RedisConnection, host, port) = 
    writePrompt(host,port)
    let command = Console.ReadLine()

    match command with
    | x when String.IsNullOrEmpty(x) -> ()
    | x when command.ToUpper() = "QUIT" -> ()
    | x -> redis.SendCommands(parse(x))
       
    readCommand (redis, host, port) |> ignore   

[<EntryPoint>]
let main argv = 
    try
        let host = "acasquete.redis.cache.windows.net"
        let port = 6380
        let password = "yE4GhOiUtmUZX1ly46rTFnNGPfFuDLfjlOdkRhph/Is="
        let useSsl = port <> 6379
        
        let redis = new RedisConnection(host, port, 30, useSsl)
        redis.Connect
        redis.MessageReceived.Add(fun (args) -> 
            let foreGroundColor = Console.ForegroundColor;
            let messageColor = 
                match args.Message with
                | x when x.StartsWith("(error)") -> ConsoleColor.Red
                | x when x.StartsWith("(nil)") -> ConsoleColor.Blue
                | x when x.StartsWith("+") -> ConsoleColor.Green
                | _ -> ConsoleColor.DarkGray

            Console.ForegroundColor <- messageColor
            Console.WriteLine(parseResponse(args.Message));
            Console.ForegroundColor <- foreGroundColor;
        )
            
        AutoAuth(redis, password, host, port) |> ignore
        readCommand (redis, host, port)
        0

    with 
        | :? System.Exception as ex -> printfn "%s" ex.Message 
                                       0

