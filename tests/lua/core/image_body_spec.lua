local core_env = require("lua.core_env")

local function to_bytes(numbers)
    local chars = {}
    for i, number in ipairs(numbers) do
        chars[i] = string.char(number)
    end
    return table.concat(chars)
end

local function padded(prefix, length)
    return prefix .. string.rep("\0", length - #prefix)
end

describe("fshttp.image_body", function()
    local image_body = core_env.load("fshttp.image_body")

    it("takes each type that starts with image/ for an image", function()
        assert.is_true(image_body.is_image("image/png"))
        assert.is_true(image_body.is_image("image/svg+xml"))
        assert.is_false(image_body.is_image("application/json"))
        assert.is_false(image_body.is_image(""))
    end)

    it("gives the extension of the type", function()
        assert.equal("png", image_body.extension("image/png"))
        assert.equal("jpg", image_body.extension("image/jpeg"))
        assert.equal("img", image_body.extension("image/x-unknown"))
    end)

    describe("the pixel size", function()
        it("reads a PNG", function()
            local header = "\137PNG\r\n\26\n"
                .. to_bytes({ 0, 0, 0, 13 })
                .. "IHDR"
                .. to_bytes({ 0, 0, 1, 44, 0, 0, 0, 100 })
            assert.same({ 300, 100 }, { image_body.pixel_size(padded(header, 33)) })
        end)

        it("reads a GIF", function()
            local header = "GIF89a" .. to_bytes({ 64, 1, 200, 0 })
            assert.same({ 320, 200 }, { image_body.pixel_size(padded(header, 20)) })
        end)

        it("reads a BMP, with a top-down height", function()
            local header = padded("BM", 18) .. to_bytes({ 10, 0, 0, 0, 246, 255, 255, 255 })
            assert.same({ 10, 10 }, { image_body.pixel_size(padded(header, 40)) })
        end)

        it("reads a JPEG past the segments before the frame", function()
            local app0 = to_bytes({ 0xFF, 0xE0, 0, 6, 1, 2, 3, 4 })
            local frame = to_bytes({ 0xFF, 0xC0, 0, 11, 8, 0, 50, 0, 70, 3, 0, 0 })
            assert.same({ 70, 50 }, { image_body.pixel_size(to_bytes({ 0xFF, 0xD8 }) .. app0 .. frame) })
        end)

        it("reads a lossy WebP", function()
            local header = "RIFF"
                .. to_bytes({ 0, 0, 0, 0 })
                .. "WEBPVP8 "
                .. padded("", 7)
                .. to_bytes({ 0x9D, 0x01, 0x2A, 40, 0, 30, 0 })
            assert.same({ 40, 30 }, { image_body.pixel_size(padded(header, 40)) })
        end)

        it("reads a lossless WebP", function()
            -- width 40 and height 30 are stored as 39 and 29 in two 14-bit fields.
            local packed = 39 + 29 * 16384
            local size = to_bytes({ packed % 256, math.floor(packed / 256) % 256, math.floor(packed / 65536) % 256, 0 })
            local header = "RIFF" .. to_bytes({ 0, 0, 0, 0 }) .. "WEBPVP8L" .. padded("", 4) .. "\47" .. size
            assert.same({ 40, 30 }, { image_body.pixel_size(padded(header, 40)) })
        end)

        it("reads an extended WebP", function()
            local header = "RIFF"
                .. to_bytes({ 0, 0, 0, 0 })
                .. "WEBPVP8X"
                .. padded("", 8)
                .. to_bytes({ 39, 0, 0, 29, 0, 0 })
            assert.same({ 40, 30 }, { image_body.pixel_size(padded(header, 40)) })
        end)

        it("gives nil for bytes that are not an image, and for a cut header", function()
            assert.is_nil(image_body.pixel_size("<svg></svg>"))
            assert.is_nil(image_body.pixel_size("\137PNG\r\n\26\n"))
            assert.is_nil(image_body.pixel_size(""))
        end)
    end)

    describe("the size line", function()
        local png = "\137PNG\r\n\26\n"
            .. to_bytes({ 0, 0, 0, 13 })
            .. "IHDR"
            .. to_bytes({ 0, 0, 0, 100, 0, 0, 0, 100 })

        it("gives the pixel size", function()
            assert.equal("100×100 px", image_body.size_line(padded(png, 33), nil))
        end)

        it("adds the reason after two spaces", function()
            assert.equal(
                "100×100 px  snacks.nvim is not installed",
                image_body.size_line(padded(png, 33), "snacks.nvim is not installed")
            )
        end)

        it("names an unknown size", function()
            assert.equal("unknown size  no reason", image_body.size_line("<svg/>", "no reason"))
        end)
    end)
end)
