// The twelve-case run-behavior corpus that docs/spec/0002-reach-a-block-anywhere.md draws its
// positions 1 to 12 from, verbatim in structure, with every URL pointed at a counting server and
// the `#r` pinned so a run is reproducible. Block order in this file IS the index order that
// `locateBlocks` reports, and PositionMatrixTests asserts against those indices.

#r "nuget: FsHttp, 15.0.3"

open FsHttp

let baseUrl = "http://127.0.0.1:8391/api/v2"


// Case 1 -- bare, top-level
http { GET "http://127.0.0.1:8391/api/v2/pokemon/pikachu" }


// Case 2 -- references a preceding pure let
http { GET $"{baseUrl}/pokemon/pikachu" }


// Case 3 -- two independent bare blocks (isolation)
http { GET "http://127.0.0.1:8391/api/v2/pokemon/bulbasaur" }

http { GET "http://127.0.0.1:8391/api/v2/pokemon/charmander" }


// Case 4 -- right-hand side of a let (block on the next line)
let squirtle = http { GET "http://127.0.0.1:8391/api/v2/pokemon/squirtle" }


// Case 5 -- right-hand side of a let (block starts on the same line)
let eevee = http { GET "http://127.0.0.1:8391/api/v2/pokemon/eevee" }


// Case 6 -- body of a ()-callable function
let getSnorlax () =
    http { GET "http://127.0.0.1:8391/api/v2/pokemon/snorlax" }


// Case 7 -- single block nested in a module
module Gen1 =
    http { GET "http://127.0.0.1:8391/api/v2/pokemon/mewtwo" }


// Case 8 -- block in a module that also has a preceding member
module Gen2 =
    let region = "johto"

    http { GET $"http://127.0.0.1:8391/api/v2/pokemon/{region}" }


// Case 9 -- inside a for-loop
for name in [ "pidgey"; "rattata" ] do
    http { GET $"http://127.0.0.1:8391/api/v2/pokemon/{name}" }


// Case 10 -- inside an if-branch
if System.DateTime.Now.Hour < 12 then
    http { GET "http://127.0.0.1:8391/api/v2/pokemon/hoothoot" }


// Case 11 -- a block that needs a value another block bound
let dexId = http { GET "http://127.0.0.1:8391/api/v2/pokemon/gengar" }

http { GET $"http://127.0.0.1:8391/api/v2/pokemon-species/{dexId}" }


// Case 12 -- block already followed by |> Request.send
http { GET "http://127.0.0.1:8391/api/v2/pokemon/lapras" } |> Request.send
