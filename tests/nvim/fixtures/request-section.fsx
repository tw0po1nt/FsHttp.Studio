// The Request fold Check of the Neovim suite. One Block POSTs a JSON body and a custom header to the
// `/echo` route of the test HTTP server. That route repeats no part of the request, so only the
// Request fold can show the posted body. `baseUrl` comes from the Sidecar that the test server
// writes beside this file.

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

http {
    POST $"{baseUrl}/echo"
    header "X-Fixture" "request-section"
    body
    json """{"posted":"request-section-fixture"}"""
}
