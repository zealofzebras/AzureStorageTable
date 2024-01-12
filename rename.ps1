# Note: Only necessary in *Windows PowerShell*.
# (These assemblies are automatically loaded in PowerShell (Core) 7+.)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Verify that types from both assemblies were loaded.
[System.IO.Compression.ZipArchiveMode]; [IO.Compression.ZipFile]

$unpackFolder = "$(Build.ArtifactStagingDirectory)/unpacked"
$repackPath = "$(Build.ArtifactStagingDirectory)/custom"
$nugetFolder = "$(Build.ArtifactStagingDirectory)/original/"

function Update-NuGetPackage {
    param(
        [string]$nugetFilePath
    )

    $nugetFileName = (Get-Item $nugetFilePath).Name

    $unpackPath = "$unpackFolder/$nugetFileName/"
    # Step 1: Unpack the NuGet package

    #"$(Build.ArtifactStagingDirectory)\unpacked"
    Write-Host "Unpacking NuGet package to $unpackPath"
    # Extract the contents of the NuGet package using System.IO.Compression
    [System.IO.Compression.ZipFile]::ExtractToDirectory($nugetFilePath, $unpackPath)

    # Step 2: Get the path to the .nuspec file
    $nuspecFilePath = Get-ChildItem $unpackPath -Recurse -Filter "*.nuspec" | Select-Object -First 1

    if ($nuspecFilePath -eq $null) {
        Write-Host "Error: No .nuspec file found in the NuGet package."
        return
    }

    # Step 3: Read the contents of the .nuspec file
    $nuspecContent = Get-Content -Path $nuspecFilePath.FullName

    # Step 4: Replace "CoreHelpers" with "CoreHelpersPlus"
    $updatedContent = $nuspecContent -replace 'CoreHelpers', 'CoreHelpersPlus'

    # Step 5: Write the updated content back to the .nuspec file
    Set-Content -Path $nuspecFilePath.FullName -Value $updatedContent
    
    # Step 7: Rename the modified .nuspec file
    $updatedNuspecFileName = $nuspecFilePath.Name -replace 'CoreHelpers', 'CoreHelpersPlus'
    $updatedNuspecFilePath = Join-Path $unpackPath $updatedNuspecFileName
    Rename-Item -Path $nuspecFilePath.FullName -NewName $updatedNuspecFileName

    # Create the repack directory if it doesn't exist
    if (-not (Test-Path -Path $repackPath)) {
        New-Item -ItemType Directory -Path $repackPath | Out-Null
    }
    # Step 8: Rename the repacked .nupkg file
    $repackNuGetFileName = $nugetFileName -replace 'CoreHelpers', 'CoreHelpersPlus'
    $repackNuGetFilePath = Join-Path $repackPath $repackNuGetFileName
    #$(Build.ArtifactStagingDirectory)/custom
    Write-Host "Repacking NuGet package to $repackPath"
    [System.IO.Compression.ZipFile]::CreateFromDirectory($unpackPath, $repackNuGetFilePath)

    Write-Host "NuGet package updated and repacked successfully."
}



# Get all NuGet package files in the folder
$nugetFiles = Get-ChildItem "$nugetFolder*.nupkg"

# Check if any files are found
if ($nugetFiles.Count -gt 0) {
    foreach ($nugetFile in $nugetFiles) {
        # Call the Update-NuGetPackage function for each file
        Update-NuGetPackage -nugetFilePath $nugetFile.FullName
    }
} else {
    Write-Host "Error: No NuGet package files found in the specified folder."
}
