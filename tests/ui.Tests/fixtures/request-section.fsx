// Fixture for the request-section check. One block POSTs a JSON body and a custom header to the
// local test server's `/echo` route, which acknowledges without repeating what was sent. The
// viewer's Request section is therefore the only place the posted body can appear, which is what
// makes the check a claim about the request as sent rather than about the response.
//
// The body and the header must match `Harness.postedBody`, `Harness.postedHeaderName`, and
// `Harness.postedHeaderValue` exactly. `baseUrl` comes from the sidecar the test server writes
// beside this file — never a hardcoded port.

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
