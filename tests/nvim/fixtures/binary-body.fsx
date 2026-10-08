// The binary body Check of the Neovim suite. The first Block gets the binary body of the `/binary`
// route. The second Block POSTs binary bytes, so the Request fold shows a Captured body. The third
// Block POSTs a stream, which the companion does not read. `baseUrl` comes from the Sidecar that the
// test server writes beside this file.

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

http { GET $"{baseUrl}/binary" }

http {
    POST $"{baseUrl}/echo"
    body
    binary [| 0uy; 1uy; 2uy; 255uy; 0uy; 128uy |]
}

http {
    POST $"{baseUrl}/echo"
    body
    stream (new MemoryStream([| 0uy; 1uy; 2uy |]))
}
