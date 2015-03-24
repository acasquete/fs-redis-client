namespace Redis.Client.Net

module Common =
    let (|Prefix|_|) (p:string) (s:string) =
        if s.StartsWith(p) then Some(s.Substring(p.Length)) else None

    let hasContent = not << System.String.IsNullOrEmpty

    let notHasContent = System.String.IsNullOrEmpty

