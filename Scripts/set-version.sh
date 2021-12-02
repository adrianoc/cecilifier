#/bin/bash!
echo $1
find . -iname "*.csproj" | xargs sed -i "s/<AssemblyVersion>[0-9]\+.[0-9]\+.[0-9]\+<\/AssemblyVersion>/<AssemblyVersion>$1<\/AssemblyVersion>/g"