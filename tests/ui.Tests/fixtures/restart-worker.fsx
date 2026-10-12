// Fixture for the worker Check of the restart checks. `/slow` hangs until the suite requests
// `/release`, and the Restart ends that Run. The pin conflicts with the version-less load of an
// earlier Run, so the companion runs this Script in a worker. `baseUrl` comes from the
// sidecar that the test server writes beside this file.

#r "nuget: FsHttp, 15.0.3"

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
