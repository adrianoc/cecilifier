# Cecilifier.Ide.Plugin for Rider and ReSharper

# Limitations / Comments

- It is not possible to run the cecilified code inside the IDE.
- Only current open document gets cecilified (as opposed to whole project); Cecilifier will fail if types in current document have references to any other types from other sources in the project (roslyn fails to resolve the types).
- Cecilified code is added as a new file in the project (not sure it is possible to add a file not associated with the project)
- No binary installation yet (need to invesigate how to build/package plugins.. hopefully it is only a gradle target)
- Error reporting is very primitive
- Hard coded executable path


# How to test

- Fix hardcoded path ini CecilifierAction.cs
- Open a console in cecilifier project root
- run `dotnet build`
- change dir to `Cecilifier.Ide.Plugin`
- run `./gradlew :runIde`