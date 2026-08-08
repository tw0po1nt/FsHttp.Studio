// Fixture for the core-path check. Two blocks request the local test server: `/json` first,
// then `/status`. The bodies differ in both URL and JSON keys, so a stale viewer render fails
// on two independent tells. `baseUrl` comes from the sidecar the test server writes beside this
// file — never a hardcoded port.

#r "nuget: FsHttp"

open System.IO
open FsHttp

let baseUrl =
    let text = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "sidecar.json"))
    let marker = "\"baseUrl\":\""
    let start = text.IndexOf(marker)

    if start < 0 then
        failwith "sidecar.json names no baseUrl"
    else
        let valueStart = start + marker.Length
        let valueEnd = text.IndexOf('"', valueStart)

        if valueEnd < 0 then
            failwith "sidecar.json baseUrl is not a JSON string"
        else
            text.Substring(valueStart, valueEnd - valueStart).TrimEnd('/')

http { GET $"{baseUrl}/json" }

http { GET $"{baseUrl}/status" }
