Cecilifier
====

About
---
Cecilifier is a tool meant to make it easier to learn how to use [Mono.Cecil](https://github.com/jbevain/cecil) a library used to manipulate MS IL. It was developed after the idea of [asmifier](https://asm.ow2.io/faq.html#Q10). You can read more details about it in its [announcement blog](https://programing-fun.blogspot.com/2019/01/making-it-easier-to-getting-started.html).

You can use it live in [this site](https://cecilifier.appspot.com/).

Feel free to send comments, issues, MR, etc; I cannot promise I'll be responsive but I'll do my best.

How to help
---
- Using it
- Finding issues
- Fixing issues
- Improving test coverage
- Improving documentation
- Adding features
- Sending feedback
- Consider donating to the project through [Patreon](https://www.patreon.com/adrianoverona)

License
---
Cecilifier is licensed under [MIT license](license.md).

Supported Features
---- 
- Attribute declaration / usage
- Type declaration
	- Class
	- Struct
	- Enum
	- Interfaces
-  Member  declaration
	- Properties
	- Methods
	- Fields
- Exception handling

Unsupported Features
---
- Generics 
	- Type /method declarations
	- Type / method instantiation

- Events
- Enumerator methods
- async/await
- Newer C# syntax (expression bodied members, elvis operator, static import, to name some)
- Much more :(

How To build
---
In order to build it you need at least .Net Core SDK 2.0

- Pull the [git repo](https://bitbucket.org/vagaus/cecilifier)
- Open a console in the folder with the pulled source code
- run dotnet build

You can run the website frontend locally by typing:
`cd Cecilifier.Web`
`dotnet run`

Then you can open a browser in `https://localhost:8001`


# How to add tests
---
First, and most importantly, tests should be self contained, clearly describing what they are testing and run quickly (unfortunately it is very likely that some of the existing tests does not meet this criteria, but nevertheless, we should strive to ;)

Existing tests work basically taking a [snippet of code](https://bitbucket.org/vagaus/cecilifier/src/master/Cecilifier.Core.Tests/TestResources/Integration/CodeBlock/Conditional/IfStatement.cs.txt), compiling it, generating the Mono.Cecil API calls to produce an assembly equivalent to the compiled snippet and finally either comparing the two assemblies or comparing the generated IL for some method with the expected output [as in this example](https://bitbucket.org/vagaus/cecilifier/src/master/Cecilifier.Core.Tests/TestResources/Integration/CodeBlock/Conditional/IfStatement.cs.il.txt). Ideally all tests should use the assembly comparison approach (as opposed to forcing developers to store the expected IL) but in some cases the comparison code would became to complex and in such cases I think it is ok to store the expected IL (anyway, I try to minimize the number of such tests).

How to report issues
---
If you hit a problem and you think it is an issue/bug in the code please follow the steps to report it:
- Search in the open / resolved issues to make sure it is not already known 
- If you cannot find anything open a new issue and do your best to:
	- add a failing test (see [`How to add tests`](#how-to-add-tests))
	- Make the title/description as clear / detailed as possible (do not assume anything; add as much details as possible)

Including a failing test is the best way to ensure the processing of the issue will happen as quick as possible and avoid any unnecessary delays.

Community
---
Since this project it tightly coupled to Mono.Cecil it makes sense to keep discussions in [Mono.Cecil google group](https://groups.google.com/forum/#!forum/mono-cecil). You can also reach me through twitter [@adrianoverona](https://twitter.com/adrianoverona)

Build Status
---
https://img.shields.io/bitbucket/pipelines/vagaus/cecilifier.svg


Potential improvements
---
For a list of potential improvements check [Cecilifier trello board](https://trello.com/b/2z5ke9Ll)

Disclaimer(s)
---
- TL;DR; Use at your own :)
- I am not a web designer/developer, so keep your expectations low (regarding the web site)
- I do not claim to be an expert in Mono.Cecil; the code certainly does not handle a lot of cases
- I do not claim that the generated code is suitable or even correct - I do have tests though :)
- I have not tried to clean up the code too much, so there are some really bad code duplication

TASKS
--
- **Open** git repo
- Cecil forum: https://groups.google.com/forum/#!forum/mono-cecil
- Post blog
- Twitter 
