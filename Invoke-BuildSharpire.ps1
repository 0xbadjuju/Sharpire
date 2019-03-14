function Invoke-BuildSharpire 
{
    [cmdletbinding()]
    param
    (       
        [ValidateSet('2.0','3.5','4.0')]
        [string]
        $DotNetVersion = '4.0',

        [string]
        $SharpireFolderPath = $pwd,

        [Parameter(Mandatory)]
        [ValidateSet("CommandLineSettings","CompileTimeSettings","CompileTimeSettings_Testing")]
        [string]
        $Configuration,

        [ValidateSet("AnyCPU","x86","x64")]
        [string]
        $Platform = "AnyCPU"
    )

    $RegPath = switch ($DotNetVersion)
    {
        '2.0' {"HKLM:\SOFTWARE\Microsoft\MSBuild\ToolsVersions\2.0"}
        '3.5' {"HKLM:\SOFTWARE\Microsoft\MSBuild\ToolsVersions\3.5"}
        '4.0' {"HKLM:\SOFTWARE\Microsoft\MSBuild\ToolsVersions\4.0"}
    }
    Write-Verbose $RegPath

    $MSBuildPath = (Get-ItemProperty -Path $RegPath).MSBuildToolsPath + "\msbuild.exe";
    Write-Verbose $MSBuildPath
    
    $SharpireFolderPath = $SharpireFolderPath + "\Sharpire.csproj"
    Write-Verbose $SharpireFolderPath

    $Platform2 = ""
    if (-not [String]::IsNullOrEmpty($Platform))
    {
        $Platform2 = "/p:Platform=" + $Platform
    }

    $Configuration2 = ""
    if (-not [String]::IsNullOrEmpty($Configuration))
    {
        $Configuration2 = "/p:Configuration=" + $Configuration
    }

    Write-Verbose "`& `"$MSBuildPath`" `"$SharpireFolderPath`" `"$Platform2`" `"$Configuration2`""    
    & "$MSBuildPath" "$SharpireFolderPath" "$Platform2" "$Configuration2"
}