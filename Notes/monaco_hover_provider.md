```javascript

// POC for displaying link for IL opcodes
// Another possibility is to add info/help for Cecil APIs
// or explanations about why/how these APIs are used.
monaco.languages.register({ id: 'mySpecialLanguage' });

monaco.languages.registerHoverProvider('mySpecialLanguage', {
	provideHover: function (model, position) {
        let atPosition = model.getWordAtPosition(position);
        let line = model.getLineContent(position.lineNumber);

        //console.log(`${atPosition.endColumn} : ${line.charAt(atPosition.endColumn-1)}`);
        if (atPosition.word === "OpCodes" && line.charAt(atPosition.endColumn - 1) === '.')
        {
            let nextWord = model.getWordAtPosition(new monaco.Position(position.lineNumber, atPosition.endColumn + 1))
                return {
                    range: new monaco.Range(
                        1,
                        1,
                        model.getLineCount(),
                        model.getLineMaxColumn(model.getLineCount())
                    ),
                    contents: [
                        { value: '**SOURCE**' },
                        { 
                            supportHtml: true,                            
                            value: `OpCode: <a href="https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.${nextWord.word}">${nextWord.word}</a>`
                        }
                    ]
                };
        }
        
        let index = line.indexOf("OpCodes.");
        while (index != -1)
        {
            let found = model.getWordAtPosition(new monaco.Position(position.lineNumber, index + 1));
            //console.log(`Index: ${index}, FoundEnd: ${found.endColumn} AtPosition: ${atPosition.startColumn}`);
            
            if (found.endColumn + 1 === atPosition.startColumn)
            {
                return {
                    range: new monaco.Range(
                        1,
                        1,
                        model.getLineCount(),
                        model.getLineMaxColumn(model.getLineCount())
                    ),
                    contents: [
                        { value: '**SOURCE**' },
                        { value: `https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.${atPosition.word}` }
                    ]
                };
            }

            index = line.indexOf("OpCodes.", found.endColumn + 1);
            console.log(`${index}, ${position.lineNumber } ${found.endColumn + 1}`);
        }

			return {
				range: new monaco.Range(
					1,
					1,
					model.getLineCount(),
					model.getLineMaxColumn(model.getLineCount())
				),
				contents: [
					{ value: '**SOURCE**' },
					{ value: `line: ${model.getLineContent(position.lineNumber)}\nword: ${atPosition.word}` }
				]
			};
	}
});

monaco.editor.create(document.getElementById('container'), {
	value: '\n\nHover over this text',
	language: 'mySpecialLanguage'
});

function xhr(url) {
	var req = null;
	return new Promise(
		function (c, e) {
			req = new XMLHttpRequest();
			req.onreadystatechange = function () {
				if (req._canceled) {
					return;
				}

				if (req.readyState === 4) {
					if ((req.status >= 200 && req.status < 300) || req.status === 1223) {
						c(req);
					} else {
						e(req);
					}
					req.onreadystatechange = function () {};
				}
			};

			req.open('GET', url, true);
			req.responseType = '';

			req.send(null);
		},
		function () {
			req._canceled = true;
			req.abort();
		}
	);
}
```