// Fixture for the Copy as curl Check of a body that the companion did not read. One block POSTs a
// stream to the local test server's `/echo` route. The companion does not read a stream, so the
// viewer has no Curl command for this Run. `baseUrl` comes from the sidecar the test server writes
// beside this file rather than from a hardcoded port.

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
    body
    stream (new MemoryStream([| 0uy; 1uy; 2uy |]))
}
