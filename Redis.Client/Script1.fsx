// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.
open System.IO
 
type STATE =
     | START  = 0
     | IDENT  = 1    // Creating an identifier token
     | STRING = 2    // Creating a string token
 
// These functions which identify the 'type' of a character are intended
// simply to make the code more readable even if some appear as overkill.
 
// Simple function that classifies a character as being alphabetic or not.
 
let Alpha = function
    | X when X >= 'a' && X <= 'z' -> true
    | X when X >= 'A' && X <= 'Z' -> true
    |'_' -> true
    | _ -> false
 
// Simple function that classifies a character as being white space or not.
 
let Space = function
    |' ' -> true
    |';' -> true
    | _ -> false
 
// Simple function that classifies a character as being a quote or not
 
let Quote = function
    |'"' -> true
    | _ -> false
 
// Simple function that converts a Char to a String.
 
let ToString C = sprintf "%c" C
let Append S C = S + ToString C
 
// Simple recursive state machine which consumes characters from the 
// supplied source and accumulates and returns complete tokens as white 
// spaces are encountered.
// The design has very few states because it currently recognizes just two 
// kinds of tokens a simple alphabetic string and a quoted string.
// The function returns a 'token' quad and the returned state is always 0 
// so that the next external invocation begins in the start state. We can 
// avoid this by wrapping the recursive function in a 'start' function 
// which is never part of any recursion.
 
let rec tokenize ((source:List<char>), state, lexeme, is_string) = 
    let C = source.Head;      // Get the next char.
    let S = List.tail source  // Get the source with that char removed.
    match (C, state) with
    | (C, STATE.START)  when Space C -> tokenize (S, state, lexeme,false)
    | (C, STATE.START)  when Alpha C -> tokenize (S, STATE.IDENT, ToString C,false)
    | (C, STATE.START)  when Quote C -> tokenize (S, STATE.STRING, "",false)
    | (C, STATE.IDENT)  when Space C -> (S, STATE.START, lexeme,false)
    | (C, STATE.IDENT)  when Alpha C -> tokenize (S, state, Append lexeme C,false)
    | (C, STATE.STRING) when Quote C -> (S, STATE.START,lexeme,true)
    | (C, STATE.STRING)              -> tokenize (S, state, Append lexeme C,false)
    | _                              -> ([],STATE.START,"",false)
 
// Get a list that contains all source file contents.
// This test file is situated in the same folder as the .fs source 
// files for this project.
 
let source = List.ofSeq("aaaaaaaaaaa \"kdkdkd\" jkjkj")
 
// Create an initial quad containing the source text and an empty 
// lexeme and start state, the bool indicates if a lexme is a 
// quoted string.
// In reality we'd support a token type enum that would avoid the
// need for such a flag, but this is purely educational code.
 
let context = (source,STATE.START,"",false)
 
// Invoke the tokenizer three times, the constructed lexeme (a string) 
// is contained within the returned triple and that triple is passed in 
// again to retrieve the next token and so on...
 
let result1 = tokenize context
let result2 = tokenize result1
let result3 = tokenize result2
let result4 = tokenize result3
let result5 = tokenize result4
 
