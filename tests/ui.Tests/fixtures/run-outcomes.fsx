// Fixture for the Run-outcomes check. Two blocks: a named 404 on the live server, then a
// connection-refused Run against the sidecar's dead port. Both URLs come from the sidecar the
// test server writes beside this file — never a hardcoded port. This is the only fixture that
// reads `deadUrl`.

#r "nuget: FsHttp"

open System.IO
open FsHttp

let private readSidecar () =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "sidecar.json"))

let private field (text: string) (name: string) =
    let marker = "\"" + name + "\":\""
    let start = text.IndexOf(marker)

    if start < 0 then
        failwith ("sidecar.json names no " + name)
    else
        let valueStart = start + marker.Length
        let valueEnd = text.IndexOf('"', valueStart)

        if valueEnd < 0 then
            failwith ("sidecar.json " + name + " is not a JSON string")
        else
            text.Substring(valueStart, valueEnd - valueStart)

let sidecar = readSidecar ()
let baseUrl = (field sidecar "baseUrl").TrimEnd('/')
let deadUrl = (field sidecar "deadUrl").TrimEnd('/')

http { GET $"{baseUrl}/notfound" }

http { GET $"{deadUrl}/" }
