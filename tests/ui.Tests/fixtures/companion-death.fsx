// Fixture for the companion-death check. Two blocks: `/slow` hangs until the suite hits
// `/release`, and the companion dies under that Run; `/json` is the recovery Run after the
// window reloads. `baseUrl` comes from the sidecar the test server writes beside this file —
// never a hardcoded port.

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

http { GET $"{baseUrl}/slow" }

http { GET $"{baseUrl}/json" }
