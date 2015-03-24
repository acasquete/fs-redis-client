open System
open Redis.Client.Net
open System.Configuration
open Redis.Client.Net.Common
open System.Text.RegularExpressions

let parseCommand (line:string) = 
    Regex.Matches(line.Trim(), @"(?<m>[\w.:{}]+)|\""(?<m>[\s\S]*)""")
    |> Seq.cast<Match>
    |> Seq.map (fun x -> x.Groups.["m"].Value)
    |> Seq.toArray

let parseResponse (line:string) = 
    match line with
    | Prefix "+" rest -> rest 
    | l when l.Contains("\r") -> line.[1..line.Length-2]
    | _ -> line

let rec readCommand (redis:RedisConnection) = 
    redis.WritePrompt
    let command = Console.ReadLine()

    match command with
    | c when notHasContent c -> ()
    | c when command.ToUpper() = "QUIT" -> () //TODO: Fix exit
    | c when command.ToUpper() = "CLEAR" -> Console.Clear()
    | c -> c |> parseCommand |> redis.SendCommands
       
    redis |> readCommand 

let getArgument (index) (name: string) (defaultValue) (args:string[]) =
    if args.Length > index then
        args.[index]
    elif hasContent ConfigurationManager.AppSettings.[name] then
        ConfigurationManager.AppSettings.[name]
    else defaultValue

let messageReceived (message) =
    let foregroundColor = Console.ForegroundColor
    let messageColor = 
        match message with
        | Prefix "(error)" _ -> ConsoleColor.Red
        | Prefix "(nil)" _   -> ConsoleColor.Blue
        | Prefix "+" _       -> ConsoleColor.Green
        | _                  -> ConsoleColor.DarkGray

    Console.ForegroundColor <- messageColor
    parseResponse(message) |> printfn "%s"
    Console.ForegroundColor <- foregroundColor

[<EntryPoint>]
let main argv = 
    try
        let host     = argv |> getArgument 0 "Host" "localhost"
        let port     = argv |> getArgument 1 "Port" "6379" |> int
        let password = argv |> getArgument 2 "Password" ""
        let alias    = argv |> getArgument 3 "Alias" ""
        let useSsl   = port <> 6379
        let redis = new RedisConnection(host, port, 30, useSsl, alias)
        redis.MessageReceived.Add(fun args -> messageReceived args)
        redis.Connect password
        redis |> readCommand
        0
    with 
        | :? System.Exception as ex -> printfn "%s" ex.Message 
                                       0
