module Renderer.Tests.JsonPrettyPrintGoldenTests

open System.IO
open System.Text.Json
open Expecto
open Renderer

let private lines (text: string list) = String.concat "\n" text

let private cases =
    [ "an object keeps the key order",
      """{"zeta":1,"alpha":2,"mid":3}""",
      Some(lines [ "{"; "  \"zeta\": 1,"; "  \"alpha\": 2,"; "  \"mid\": 3"; "}" ])
      "objects and arrays nest",
      """{"name":"snorlax","moves":["rest","snore"],"stats":{"hp":160,"speed":30}}""",
      Some(
          lines
              [ "{"
                "  \"name\": \"snorlax\","
                "  \"moves\": ["
                "    \"rest\","
                "    \"snore\""
                "  ],"
                "  \"stats\": {"
                "    \"hp\": 160,"
                "    \"speed\": 30"
                "  }"
                "}" ]
      )
      "an empty object and an empty array stay on one line",
      "{\"a\":{},\"b\":[],\"c\":[ ],\"d\":{ \n }}",
      Some(lines [ "{"; "  \"a\": {},"; "  \"b\": [],"; "  \"c\": [],"; "  \"d\": {}"; "}" ])
      "an array is the top value",
      """[1,[2,[3]],{"k":null}]""",
      Some(
          lines
              [ "["
                "  1,"
                "  ["
                "    2,"
                "    ["
                "      3"
                "    ]"
                "  ],"
                "  {"
                "    \"k\": null"
                "  }"
                "]" ]
      )
      "a number keeps its source text",
      "[1e3, -0.50, 1E+2, 0]",
      Some(lines [ "["; "  1e3,"; "  -0.50,"; "  1E+2,"; "  0"; "]" ])
      "a string keeps its escapes",
      """{"quote":"a\"b","unicode":"\u00e9\ud83d\ude00","slash":"\/","raw":"é😀"}""",
      Some(
          lines
              [ "{"
                "  \"quote\": \"a\\\"b\","
                "  \"unicode\": \"\\u00e9\\ud83d\\ude00\","
                "  \"slash\": \"\\/\","
                "  \"raw\": \"é😀\""
                "}" ]
      )
      "the literals stay as they are", "[true,false,null]", Some(lines [ "["; "  true,"; "  false,"; "  null"; "]" ])
      "a string is the top value", "\"just a string\"", Some "\"just a string\""
      "a number is the top value", " 42 ", Some "42"
      "the space around and between the tokens goes",
      "\r\n\t{ \"a\" :\n 1 ,\"b\":[ 2 ] }\n",
      Some(lines [ "{"; "  \"a\": 1,"; "  \"b\": ["; "    2"; "  ]"; "}" ])
      "a pretty-printed body stays the same",
      lines [ "{"; "  \"a\": ["; "    1"; "  ]"; "}" ],
      Some(lines [ "{"; "  \"a\": ["; "    1"; "  ]"; "}" ])
      "text after the value gives no output", """{"a":1} x""", None
      "an unterminated string gives no output", """{"a":"b""", None
      "a comma before a closing bracket gives no output", "[1,]", None
      "a comma before a closing brace gives no output", """{"a":1,}""", None
      "a missing colon gives no output", """{"a" 1}""", None
      "a bad escape gives no output", "\"\\x\"", None
      "a short unicode escape gives no output", "\"\\u12\"", None
      "a single-quoted key gives no output", "{'a':1}", None
      "an empty body gives no output", "", None
      "a body of only space gives no output", " \n ", None ]

let private goldenFixture () =
    JsonSerializer.SerializeToUtf8Bytes
        {| cases =
            [ for name, body, _ in cases ->
                  {| body = body
                     name = name
                     pretty = Json.tryPrettyPrint body |> Option.toObj |} ] |}

[<Tests>]
let tests =
    testList
        "JSON pretty-printer"
        [ for name, body, expected in cases do
              test name { Expect.equal (Json.tryPrettyPrint body) expected name }

          test "the JSON pretty-printer matches its Golden fixture" {
              GoldenFixture.verify (Path.Combine("json", "pretty-print.json")) (goldenFixture ())
          } ]
