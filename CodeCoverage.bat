@echo off
set OPENCOVER_DIR=d:\utils\OpenCover
set OUTPUT_DIR=Ceciifier.Core.Tests\bin\Debug


pushd %OUTPUT_DIR%
%OPENCOVER_DIR%\OpenCover.Console.exe -register:user -target:"..\..\..\libs\NUnit-2.5.10.11092\bin\net-2.0\nunit-console.exe" -targetargs:"Ceciifier.Core.Tests.dll" -filter:"+[Cecilifier*]* -[Ceciifier*]*" -output:.\cecilifier.tests.ouptut.xml 
popd

set TARGET_REPORT_DIR=%OUTPUT_DIR%\CodeCoverage
set REPORT_FILE=%TARGET_REPORT_DIR%\index.htm

%OPENCOVER_DIR%\ReportGenerator\bin\ReportGenerator.exe -reports:"%OUTPUT_DIR%\cecilifier.tests.ouptut.xml" -targetdir:%TARGET_REPORT_DIR%\

if exist "%REPORT_FILE%" (
	start %REPORT_FILE%
) else (
	echo Could not find %REPORT_FILE% report file
)

set TARGET_REPORT_DIR=
set OPENCOVER_DIR=
set OUTPUT_DIR=
set REPORT_FILE=