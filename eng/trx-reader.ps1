function Read-TrxDocument {
    param([Parameter(Mandatory)][string]$Path)

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.IgnoreComments = $true
    $document = [System.Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $reader = $null
    try {
        $reader = [System.Xml.XmlReader]::Create($Path, $settings)
        $document.Load($reader)
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
    }
    return ,$document
}

function Read-TrxDuration {
    param([Parameter(Mandatory)][System.Xml.XmlElement]$Result)

    [TimeSpan]$duration = [TimeSpan]::Zero
    $text = $Result.GetAttribute('duration')
    if (-not [string]::IsNullOrWhiteSpace($text) -and
        -not [TimeSpan]::TryParse($text, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$duration)) {
        throw 'Malformed MSTest TRX result contains an invalid duration.'
    }
    if ($duration.Ticks -lt 0) {
        throw 'Malformed MSTest TRX result contains a negative duration.'
    }
    return $duration
}
