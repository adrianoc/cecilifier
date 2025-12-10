using System.Resources;

namespace Cecilifier.Web;

public class CecilifierModel
{
    private const string ProjectContents = """
                                           <Project Sdk="Microsoft.NET.Sdk">
                                               <PropertyGroup>
                                                   <OutputType>Exe</OutputType>
                                                   <TargetFramework>net9.0</TargetFramework>
                                               </PropertyGroup>{0}
                                           </Project>
                                           """;

    private const string NugetConfigForCecilifiedProject = """
                                                           <configuration>
                                                               <packageSources>
                                                                   <add key="CecilifierCodeGenerators" value="./NugetLocalRepo/" />
                                                               </packageSources>
                                                           </configuration>
                                                           """;

    public static (string fileName, string contents)[] CompressedProjectContentsFor(string cecilifiedCode, string targetApi)
    {
        if (targetApi == "Mono.Cecil")
        {
            return
            [
                ("Program.cs", cecilifiedCode),
                ("Cecilified.csproj", ProjectContentsForTargetApi(targetApi)),
                ("nuget.config", CecilifierModel.NugetConfigForCecilifiedProject),
                ("NugetLocalRepo/Cecilifier.TypeMapGenerator.1.0.0.nupkg", "file-relative-path://Cecilifier.TypeMapGenerator.1.0.0.nupkg"),
                NameAndContentFromResource("Cecilifier.Web.Runtime")
            ];
        }
        
        return            
        [
            ("Program.cs", cecilifiedCode),
            ("Cecilified.csproj", ProjectContentsForTargetApi(targetApi))
        ];
 
    }

    private static string ProjectContentsForTargetApi(string targetApi)
    {
        var projectItems = targetApi == "Mono.Cecil" 
            ? """

              <ItemGroup>
                    <PackageReference Include="Mono.Cecil" Version="0.11.6" />
                    <PackageReference Include="Cecilifier.TypeMapGenerator" Version="1.0.0" />
              </ItemGroup>
              """ 
            : string.Empty;
            
        return string.Format(ProjectContents, projectItems);
    }
    
    static (string fileName, string contents) NameAndContentFromResource(string resourceName)
    {
        var rm = new ResourceManager(resourceName, typeof(CecilifierModel).Assembly);
        var contents = rm.GetString("TypeHelpers");
        return ("RuntimeHelper.cs", contents);
    }
}
