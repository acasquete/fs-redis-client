open System
open Redis.Client.Net
open System.Configuration
open Redis.Client.Net.Common

let writePrompt (host, port) =
    (host, port) ||> printf "%s:%d> " 

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

let rec readCommand (redis:RedisConnection, host, port) = 
    writePrompt(host,port)
    let command = Console.ReadLine()

    match command with
    | c when String.IsNullOrEmpty(c) -> ()
    | c when command.ToUpper() = "QUIT" -> ()
    | c when command.ToUpper() = "CLEAR" -> Console.Clear()
    | c -> redis.SendCommands(parse(c))
       
    readCommand (redis, host, port)

let getArgumentValue (name:string, defaultValue, args:string[], index) =
    if args.Length > index then
        string args.[index]
    elif String.IsNullOrEmpty(ConfigurationManager.AppSettings.[name]) = false then
        string Configuration.ConfigurationManager.AppSettings.[name]
    else defaultValue

let messageReceived (args:RedisMessageReceiveEventArgs<string>) =
    let foregroundColor = Console.ForegroundColor
    let messageColor = 
        match args.Message with
        | Prefix "(error)" _ -> ConsoleColor.Red
        | Prefix "(nil)" _   -> ConsoleColor.Blue
        | Prefix "+" _       -> ConsoleColor.Green
        | _                  -> ConsoleColor.DarkGray

    Console.ForegroundColor <- messageColor
    (parseResponse(args.Message)) |> printfn "%s"
    Console.ForegroundColor <- foregroundColor

[<EntryPoint>]
let main argv = 
    try
        let host = getArgumentValue("Host", "localhost", argv, 0 )
        let port = getArgumentValue("Port", "6379", argv, 1 ) |> int
        let password = getArgumentValue("Password", String.Empty, argv, 2 )
        let useSsl = port <> 6379
        
        let redis = new RedisConnection(host, port, 30, useSsl)
        redis.Connect
        redis.MessageReceived.Add(fun args -> messageReceived args)
            
        AutoAuth(redis, password, host, port) |> ignore
        readCommand (redis, host, port)
        0

    with 
        | :? System.Exception as ex -> printfn "%s" ex.Message 
                                       0
