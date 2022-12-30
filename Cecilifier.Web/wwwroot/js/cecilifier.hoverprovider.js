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
           { value: `<b>${opCode}</b>` },
           { 
                supportHtml: true,
                value: `${opCodes.find((candidate) => candidate.name === opCode).description}`
                
                //TODO: It would be interesting to be able to link to the documentation but it fails due to CORS
                //value: `OpCode: <a href="https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.${opCode}">${opCode}</a>`
            }
        ]
    };
}