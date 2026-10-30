param([switch]$IncludeRealBuild)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\MaestroOscService\MaestroOscService.csproj'
& dotnet run --project $project -p:MockOnly=true -- --self-test
if ($LASTEXITCODE -ne 0) { throw 'C# mock self-tests or compilation failed.' }
& python -m unittest discover -s (Join-Path $PSScriptRoot '..\tests') -p 'test_*.py' -v
if ($LASTEXITCODE -ne 0) { throw 'Python codec tests failed.' }
if ($IncludeRealBuild) {
    & dotnet clean $project -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Clean failed.' }
    & dotnet build $project -c Release -p:MockOnly=false
    if ($LASTEXITCODE -ne 0) { throw 'Real HIDMaestro build failed.' }
}
