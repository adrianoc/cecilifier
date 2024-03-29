```javascript
// POC for displaying link for IL opcodes
// Another possibility is to add info/help for Cecil APIs
// or explanations about why/how these APIs are used.
/*
 * For some reason the link to the IL docs in the popup is not clickable.
 * 
 * Anyway, it may worth trying to add a source generator that:
 * 1. Using Nuget APIs, figure out the path of System.Reflection.Primitives assembly used
 * 1. Extracts XML documentation for all opcodes (fields) of System.Reflection.Emit.OpCodes (https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes?view=net-7.0)
 * 1. Expose that docs to the frontend
 * 1. Frontend shows the actual doc instead of a link (but it would be nice to solve the link 
 *    issue and provide it also)
 * 
 * docs:
 * - Source generators
 *  - 
 * - Nuget APIs 
 *  - https://learn.microsoft.com/en-us/nuget/reference/nuget-client-sdk
 *  - https://www.daveaglick.com/posts/exploring-the-nuget-v3-libraries-part-1
 * 
 */
function initializeHoverProvider() {
    monaco.languages.registerHoverProvider('csharp', {
        provideHover: function (model, position) {
            let atPosition = model.getWordAtPosition(position);
            let line = model.getLineContent(position.lineNumber);

            if (atPosition === null)
                return;

            //console.log(`${atPosition.endColumn} : ${line.charAt(atPosition.endColumn-1)}`);
            if (atPosition.word === "OpCodes" && line.charAt(atPosition.endColumn - 1) === '.')
            {
                let nextWord = model.getWordAtPosition(new monaco.Position(position.lineNumber, atPosition.endColumn + 1))
                return getIt(model, nextWord.word);
            }
            
            let index = line.indexOf("OpCodes.");
            while (index != -1)
            {
                let found = model.getWordAtPosition(new monaco.Position(position.lineNumber, index + 1));
                //console.log(`Index: ${index}, FoundEnd: ${found.endColumn} AtPosition: ${atPosition.startColumn}`);
                
                if (found.endColumn + 1 === atPosition.startColumn)
                {
                    return getIt(model, atPosition.word);
                }

                index = line.indexOf("OpCodes.", found.endColumn + 1);
                //console.log(`${index}, ${position.lineNumber } ${found.endColumn + 1}`);
            }
        }
    });
}

function getIt(model, opCode) {
    return {        
        contents: [
           { value: '**Link to documentation for IL opcode**' },
           { 
                supportHtml: true,                            
                value: `OpCode: <a href="https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.${opCode}">${opCode}</a>`
            }
        ]
    };
}
```