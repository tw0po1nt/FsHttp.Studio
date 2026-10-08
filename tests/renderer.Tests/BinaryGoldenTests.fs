module Renderer.Tests.BinaryGoldenTests

open System.IO
open System.Text
open System.Text.Json
open Expecto
open Renderer.Core

let private utf8 (s: string) = Encoding.UTF8.GetBytes s

let private repeat (count: int) (b: byte) = Array.create count b

let private printableAndNot =
    [| 0x1fuy
       0x20uy
       0x41uy
       0x7euy
       0x7fuy
       0x80uy
       0xffuy
       0x22uy
       0x26uy
       0x3cuy
       0x60uy |]

let private binaryTestCases =
    [ "an empty body", [||], false
      "plain text", utf8 "Hello, world!", false
      "text with a tab, a newline, and a carriage return", utf8 "a\tb\r\nc\n", false
      "one NUL byte in text", Array.append (utf8 "text") [| 0uy |], true
      "a PNG signature", [| 0x89uy; 0x50uy; 0x4euy; 0x47uy; 0x0duy; 0x0auy; 0x1auy; 0x0auy |], false
      "control bytes at 30 percent", Array.append (repeat 3 1uy) (repeat 7 0x41uy), false
      "control bytes above 30 percent", Array.append (repeat 31 1uy) (repeat 69 0x41uy), true
      "only control bytes", repeat 4 0x1buy, true
      "bytes above 127 with no control byte", [| 0x80uy; 0xc3uy; 0xa9uy; 0xffuy |], false ]

let private hexDumpCases =
    [ "an empty body", [||]
      "one byte", [| 0x41uy |]
      "printable and unprintable bytes", printableAndNot
      "one full line", Array.init 16 byte
      "one byte after a full line", Array.init 17 byte
      "256 bytes", Array.init 256 byte
      "257 bytes", Array.init 257 byte
      "300 bytes", Array.init 300 (fun i -> byte (255 - i % 256)) ]

let private encode (value: obj) =
    JsonSerializer.SerializeToUtf8Bytes value

let private binaryTestFixture () =
    encode
        {| cases =
            [ for name, bytes, _ in binaryTestCases ->
                  {| bytes = Array.map int bytes
                     looksBinary = looksBinary bytes
                     name = name |} ] |}

let private hexDumpFixture () =
    encode
        {| cases =
            [ for name, bytes in hexDumpCases ->
                  {| bytes = Array.map int bytes
                     hexDump = hexDump bytes
                     name = name |} ] |}

[<Tests>]
let tests =
    testList
        "binary body Golden fixtures"
        [ for name, bytes, expected in binaryTestCases do
              test (sprintf "the binary test gives %b for %s" expected name) {
                  Expect.equal (looksBinary bytes) expected name
              }

          test "the hex dump puts 16 bytes on a line, with the offset and the printable bytes" {
              Expect.equal
                  (hexDump (Array.init 17 byte))
                  ("00000000  00 01 02 03 04 05 06 07 08 09 0a 0b 0c 0d 0e 0f  ................\n"
                   + "00000010  10                                               .")
                  "the second line pads the hex column"
          }

          test "the hex dump shows 256 bytes and counts the bytes that it does not show" {
              let lines = (hexDump (Array.init 300 byte)).Split('\n')
              Expect.hasLength lines 17 "16 lines of bytes and the count line"
              Expect.equal lines.[16] "… (44 more bytes)" "the count line"
          }

          test "the binary test matches its Golden fixture" {
              GoldenFixture.verify (Path.Combine("binary", "binary-test.json")) (binaryTestFixture ())
          }

          test "the hex dump matches its Golden fixture" {
              GoldenFixture.verify (Path.Combine("binary", "hex-dump.json")) (hexDumpFixture ())
          } ]
