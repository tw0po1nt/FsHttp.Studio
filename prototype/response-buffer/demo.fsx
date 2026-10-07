// PROTOTYPE: the script that the Response buffer mock shows beside its result.
// The mock reads saved results from fixtures/. Nothing here runs.

#r "nuget: FsHttp"

open FsHttp

http { GET "https://api.github.com/repos/fsprojects/FsHttp" }

http { GET "https://httpbin.org/image/png" }

http { GET "https://example.com/" }

let probe: int = "zero"

http { GET "http://127.0.0.1:9/" }
