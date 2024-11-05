function initializeHoverProvider() {
    monaco.languages.registerHoverProvider('csharp', {
        provideHover: function (model, position) {
            let atPosition = model.getWordAtPosition(position);
            let line = model.getLineContent(position.lineNumber);

            if (atPosition === null)
                return;

            if (atPosition.word === "OpCodes" && line.charAt(atPosition.endColumn - 1) === '.')
            {
                // mouse cursor is at any char of 'OpCodes.' the next word is the opcode we are interested in.
                let nextWord = model.getWordAtPosition(new monaco.Position(position.lineNumber, atPosition.endColumn + 1))
                return getOpcodeHoverDataFor(model, nextWord.word);
            }
            
            let index = line.indexOf("OpCodes.");
            while (index !== -1)
            {
                // mouse cursor is potentially over an opcode name (for instance at `Ldarg_0` in `OpCodes.Ldarg_0`)
                // in this case check whether the end position of `found` (which should represent the word `OpCodes.`)
                // is the char that precedes the word in `atPosition`...
                let found = model.getWordAtPosition(new monaco.Position(position.lineNumber, index + 1));
                if (found.endColumn + 1 === atPosition.startColumn)
                {
                    return getOpcodeHoverDataFor(model, atPosition.word);
                }

                index = line.indexOf("OpCodes.", found.endColumn + 1);
            }
        }
    });
}

function getOpcodeHoverDataFor(model, opCode) {
    let description = opCodes.find((candidate) => candidate.name === opCode).description;
    return {        
        contents: [
           { value: `<b>${opCode}</b>` },
           {
               supportHtml: true,
               value: `<a href="https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.${opCode}">${opCode}</a><br/>${description}`,
            }
        ]
    };
}