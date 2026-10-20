param([string]$OutDir = "$env:TEMP\kyapputone-fixtures")

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue)?.Source
if (-not $ffmpeg) {
    throw "ffmpeg not found on PATH. Install ffmpeg or point this script at a binary."
}

& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=440:duration=15" -c:a flac "$OutDir\tone.flac"
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=660:duration=15" -c:a libmp3lame "$OutDir\two.mp3"
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=880:duration=15" -c:a aac "$OutDir\three.m4a"

Copy-Item "$OutDir\tone.flac" "$OutDir\one-日本語-ąćęłńóśźż.flac"
Copy-Item (Join-Path $PSScriptRoot '..\Assets\AppIcon.png') "$OutDir\cover.png"
[System.IO.File]::WriteAllBytes("$OutDir\broken-cover.bin", [byte[]](0x00, 0x01, 0x02, 0x03))

Get-ChildItem $OutDir
