open System.Text.RegularExpressions

//let s = "eval \"return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}\" 2 key1 key2 first second"
let s = "set my:key.{}... \"12345 6788\""

let parseCommand (line:string) = 
    Regex.Matches(line.Trim(), @"(?<m>[\w.:{}]+)|\""(?<m>[\s\S]*)""")
    |> Seq.cast<Match>
    |> Seq.map (fun x -> x.Groups.["m"].Value)
    |> Seq.toArray

parseCommand s



