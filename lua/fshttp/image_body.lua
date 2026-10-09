-- The image test, the file extension, and the pixel size of an image body. Pure, so the Lua core
-- suite can load it.
local M = {}

M.snacks_missing_reason = "snacks.nvim is not installed"

-- The extension that snacks.nvim needs to open the file of each common image type.
local extensions = {
    ["image/png"] = "png",
    ["image/jpeg"] = "jpg",
    ["image/jpg"] = "jpg",
    ["image/gif"] = "gif",
    ["image/webp"] = "webp",
    ["image/bmp"] = "bmp",
    ["image/svg+xml"] = "svg",
    ["image/avif"] = "avif",
    ["image/tiff"] = "tiff",
    ["image/x-icon"] = "ico",
    ["image/vnd.microsoft.icon"] = "ico",
}

-- The VSCode renderer core treats each `image/` type as an image.
---@param content_type string the type with no parameters
---@return boolean
function M.is_image(content_type)
    return content_type:sub(1, #"image/") == "image/"
end

---@param content_type string the type with no parameters
---@return string
function M.extension(content_type)
    return extensions[content_type] or "img"
end

---@param bytes string
---@param first integer 1-based
---@return integer
local function big_endian16(bytes, first)
    local a, b = bytes:byte(first, first + 1)
    return a * 256 + b
end

---@param bytes string
---@param first integer 1-based
---@param count integer
---@return integer
local function little_endian(bytes, first, count)
    local value = 0
    for i = count - 1, 0, -1 do
        value = value * 256 + (bytes:byte(first + i) or 0)
    end
    return value
end

---@param bytes string
---@return integer?, integer?
local function png_size(bytes)
    if #bytes < 24 or bytes:sub(1, 8) ~= "\137PNG\r\n\26\n" then
        return nil
    end
    local width = big_endian16(bytes, 17) * 65536 + big_endian16(bytes, 19)
    local height = big_endian16(bytes, 21) * 65536 + big_endian16(bytes, 23)
    return width, height
end

---@param bytes string
---@return integer?, integer?
local function gif_size(bytes)
    if #bytes < 10 or bytes:sub(1, 4) ~= "GIF8" then
        return nil
    end
    return little_endian(bytes, 7, 2), little_endian(bytes, 9, 2)
end

---@param bytes string
---@return integer?, integer?
local function bmp_size(bytes)
    if #bytes < 26 or bytes:sub(1, 2) ~= "BM" then
        return nil
    end
    local height = little_endian(bytes, 23, 4)
    if height >= 2 ^ 31 then
        height = 2 ^ 32 - height
    end
    return little_endian(bytes, 19, 4), height
end

-- Walks the marker segments until a start-of-frame marker.
---@param bytes string
---@return integer?, integer?
local function jpeg_size(bytes)
    if #bytes < 4 or bytes:sub(1, 2) ~= "\255\216" then
        return nil
    end
    local at = 3
    while at + 3 <= #bytes do
        if bytes:byte(at) ~= 0xFF then
            return nil
        end
        local marker = bytes:byte(at + 1)
        if marker == 0xFF then
            at = at + 1
        elseif marker == 0x01 or (marker >= 0xD0 and marker <= 0xD8) then
            at = at + 2
        else
            local is_frame = marker >= 0xC0 and marker <= 0xCF and marker ~= 0xC4 and marker ~= 0xC8 and marker ~= 0xCC
            if is_frame then
                if at + 8 > #bytes then
                    return nil
                end
                return big_endian16(bytes, at + 7), big_endian16(bytes, at + 5)
            end
            at = at + 2 + big_endian16(bytes, at + 2)
        end
    end
    return nil
end

---@param bytes string
---@return integer?, integer?
local function webp_size(bytes)
    if #bytes < 30 or bytes:sub(1, 4) ~= "RIFF" or bytes:sub(9, 12) ~= "WEBP" then
        return nil
    end
    local kind = bytes:sub(13, 16)
    if kind == "VP8 " then
        return little_endian(bytes, 27, 2) % 16384, little_endian(bytes, 29, 2) % 16384
    elseif kind == "VP8L" then
        local packed = little_endian(bytes, 22, 4)
        return packed % 16384 + 1, math.floor(packed / 16384) % 16384 + 1
    elseif kind == "VP8X" then
        return little_endian(bytes, 25, 3) + 1, little_endian(bytes, 28, 3) + 1
    end
    return nil
end

-- The width and the height in pixels, or nil when the bytes are not a PNG, GIF, BMP, JPEG, or WebP
-- image, or when the header is cut short.
---@param bytes string
---@return integer?, integer?
function M.pixel_size(bytes)
    for _, reader in ipairs({ png_size, gif_size, bmp_size, jpeg_size, webp_size }) do
        local width, height = reader(bytes)
        if width and height then
            return width, height
        end
    end
    return nil
end

-- The reason when response_buffer.images is false.
M.images_off_reason = "images are off (response_buffer.images)"

-- The line below the Body title: the pixel size, then the reason when no image can show.
---@param bytes string
---@param reason string? why no image can show, or nil when one can
---@return string
function M.size_line(bytes, reason)
    local width, height = M.pixel_size(bytes)
    local size = width and string.format("%d×%d px", width, height) or "unknown size"
    if reason then
        return size .. "  " .. reason
    end
    return size
end

return M
