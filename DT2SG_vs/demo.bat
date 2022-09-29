
powershell -command "Expand-Archive -Force '%~dp0demo.zip' '%~dp0'"
DT2SG_vs.exe -r .\demo\source -m .\demo\source_metadata\metadata_example.csv
cd demo
git log SourceCode