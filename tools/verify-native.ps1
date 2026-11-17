[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference='Stop'
$file=(Resolve-Path -LiteralPath $Executable).Path
$stream=[IO.File]::OpenRead($file)
$reader=[IO.BinaryReader]::new($stream)
try {
    if($reader.ReadUInt16() -ne 0x5A4D){throw 'Not a PE executable.'}
    $stream.Position=0x3c
    $pe=$reader.ReadUInt32()
    $stream.Position=$pe
    if($reader.ReadUInt32() -ne 0x4550){throw 'Invalid PE signature.'}
    if($reader.ReadUInt16() -ne 0x8664){throw 'Expected native x64 executable.'}
    $sections=$reader.ReadUInt16()
    $stream.Position=$pe+20
    $optionalSize=$reader.ReadUInt16()
    $optional=$pe+24
    $stream.Position=$optional
    if($reader.ReadUInt16() -ne 0x20B){throw 'Expected PE32+ executable.'}
    $directories=$optional+112
    $stream.Position=$directories+14*8
    if($reader.ReadUInt32() -ne 0 -or $reader.ReadUInt32() -ne 0){throw 'CLR header found: executable is managed.'}
    $stream.Position=$directories+8
    $importRva=$reader.ReadUInt32()
    $sectionMap=@()
    for($i=0;$i -lt $sections;$i++){
        $stream.Position=$optional+$optionalSize+40*$i+8
        $virtualSize=$reader.ReadUInt32()
        $rva=$reader.ReadUInt32()
        $rawSize=$reader.ReadUInt32()
        $raw=$reader.ReadUInt32()
        $sectionMap+=@{Rva=$rva;Size=[Math]::Max($virtualSize,$rawSize);Raw=$raw}
    }
    function Offset([uint32]$rva){
        foreach($s in $sectionMap){if($rva -ge $s.Rva -and $rva -lt $s.Rva+$s.Size){return [long]($s.Raw+$rva-$s.Rva)}}
        throw "Unmapped PE RVA: $rva"
    }
    $imports=@()
    $table=Offset $importRva
    for($i=0;$i -lt 256;$i++){
        $stream.Position=$table+20*$i
        $descriptor=@(1..5 | ForEach-Object {$reader.ReadUInt32()})
        if(($descriptor | Where-Object {$_ -ne 0}).Count -eq 0){break}
        $stream.Position=Offset $descriptor[3]
        $name=''
        for($n=0;$n -lt 256;$n++){
            $c=$reader.ReadByte()
            if($c -eq 0){break}
            $name += [char]$c
        }
        $imports+=$name
    }
    $allowed=@('KERNEL32.dll','USER32.dll','GDI32.dll','ADVAPI32.dll','SHELL32.dll','ole32.dll',
        'OLEAUT32.dll','WINMM.dll','OPENGL32.dll','windowscodecs.dll','COMDLG32.dll','IMM32.dll',
        'VERSION.dll','WS2_32.dll','bcrypt.dll','api-ms-win-core-synch-l1-2-0.dll',
        # Statically linked SDL3: device enumeration for input and audio.
        'SETUPAPI.dll','CFGMGR32.dll','HID.dll','dwmapi.dll','UxTheme.dll','SHLWAPI.dll')
    foreach($name in $imports){
        if($name -notin $allowed){throw "Unexpected runtime dependency: $name"}
    }
    Write-Host "Native verification: x64, no CLR; imports only Windows DLLs: $($imports -join ', ')"
} finally {$reader.Dispose();$stream.Dispose()}
