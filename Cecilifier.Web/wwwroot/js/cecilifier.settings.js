/** Settings methods **/

// Keep it in sync with ElementKind.cs
const ElementKind =
{
    Class : 0,
    Struct : 1,
    Interface : 2,
    Enum : 3,
    Method: 4,
    Delegate: 5,
    Property: 6,
    Field: 7,
    Event: 8,
    Constructor: 9,
    StaticConstructor: 10,
    LocalVariable: 11,
    Parameter: 12,
    MemberDeclaration: 13,
    MemberReference: 14,
    Label: 15,
    Attribute: 16,
    IL: 17,
    GenericParameter: 18,
    GenericInstance: 19
};

// Keep it in sync with NameOptions.cs
const NamingOptions =
{
    DifferentiateDeclarationsAndReferences: 0x1,
    PrefixInstructionsWithILOpCodeName: 0x2,
    AppendElementNameToVariables: 0x4,
    PrefixVariableNamesWithElementKind: 0x8,
    SuffixVariableNamesWithUniqueId: 0x10,
    SeparateCompoundWords: 0x20,
    CamelCaseElementNames: 0x40,
    AddCommentsToMemberDeclarations: 0x80,
    IncludeSourceInErrorReports : 0x100,
    StoreSettingsLocally: 0x200
};

class SettingsManager {
    constructor(sampleEditor, settings) {
        this.settings = settings;
        this.settingsTable = null;
        this.optionalFormats = [];
        this.namingOptions = new Map();
        this.sampleEditor = sampleEditor;
        this.id = 0;
        
        for(let i = 0; i < this.settings.length; i++) {
            this.settings[i].setInfo(this, i);
        }
    }

    updateExample(pos, newValue) {
        this.settings[pos].updateExample(newValue);
    }
    
    updateConditionalFormat(groupIndex, replace, valueExtractor) {
        for(let si = 0; si < this.settings.length; si++) {
            this.settings[si].updateConditionalFormat(groupIndex, replace, this.optionalFormats, valueExtractor);
        }
    }

    uniqueId() {
        return 'sm-' + this.id++
    }

    initialize(settingsTable) {        
        if (this.settingsTable != null)
            return false;

        this.settingsTable = settingsTable;
        for(let i = 0; i < this.settings.length; i++) {
            const configRow = settingsTable.insertRow(i + 1);
            configRow.addEventListener('mouseover', this.settings[i].toggleGutter.bind(this.settings[i]));
            this.settings[i].setDOMOwner(configRow);

            const configLabelCell = configRow.insertCell(0);
            configLabelCell.innerText = this.settings[i].name;

            const configInputCell = configRow.insertCell(1);
            configInputCell.innerHTML = `<td><input class="cecilifier-input" type="text" placeholder="${this.settings[i].placeholder}" value="${this.settings[i].prefix}" onchange="settings.updateExample(${i}, this.value);"></input></td>`;
        }

        return true;
    }
    
    onShowHandlerFor(configRow) {
        return (instance) => {
            let mouseMoved = false;
            let timeoutHandler = null;

            timeoutHandler = () => {
                if (mouseMoved) {
                    instance.hide();
                    configRow.onmousemove = null;
                }
                else {
                    setTimeout(timeoutHandler, 500);
                }
            };

            configRow.onmousemove = () => { mouseMoved = true; };
            setTimeout(timeoutHandler, 500);
        }
    }

    /*
     * adds a checkbox that enables/disables a specific group in the variable name
     * 
     * @param {string} description - checkbox text
     * @param {number} groupIndex - index of the affected group in the variable name.
     * @callback valueExtractor - function that takes a @type Settings as a parameter and returns the string to be shown.
     * @callback callback - function .
     */
    addConditionalFormat(namingOption, description, groupIndex, tooltip, valueExtractor, callback) {
        const configRow = this.settingsTable.insertRow(-1);

        configRow.id = this.uniqueId();
        tippy(`#${configRow.id}`, {
            content: tooltip,
            placement: 'top',
            interactive: true,
            allowHTML: false,
            theme: 'cecilifier-tooltip',
            delay: 200,
            onShow : this.onShowHandlerFor(configRow)
        });
        
        const configCell = configRow.insertCell(0);
        configCell.setAttribute("colspan", 2);
        
        configCell.innerHTML = `<input type="checkbox"> ${description}</input>`;

        const optionalInput = configCell.firstChild;
        optionalInput.checked = true;

        const optionalMemberNameFormat = new OptionalMemberNameFormat(this, groupIndex, optionalInput, valueExtractor);

        optionalInput.onchange = () => {
            callback(optionalInput.checked);

            const validationMessage = this.validateOptionalFormat();
            if (validationMessage != null) {
                const textElement = document.getElementById("cecilifier-validation-msg");
                textElement.innerText = validationMessage;

                const navNode = document.querySelector("#cecilifier-validation");
                navNode.classList.remove("hidden");

                optionalInput.checked = true;
                return;
            }

            this.updateConditionalFormat(groupIndex, false, optionalMemberNameFormat.extractValue.bind(optionalMemberNameFormat));
        };

        this.optionalFormats.push(optionalInput);
        this.namingOptions.set(namingOption, {
            getter : () => { return optionalInput.checked; },
            setter : (value) => { optionalInput.checked = value ; },
        });

        return optionalMemberNameFormat;
    }

    addBooleanOption(namingOption, description, tooltip, callback){
        const configRow = this.settingsTable.insertRow(-1);

        configRow.id = this.uniqueId();
        tippy(`#${configRow.id}`, {
            content: tooltip,
            placement: 'top',
            interactive: true,
            allowHTML: false,
            theme: 'cecilifier-tooltip',
            delay: 200,
            onShow : this.onShowHandlerFor(configRow)
        });
        
        const configCell = configRow.insertCell(0);
        configCell.setAttribute("colspan", 2);
        configCell.innerHTML = `<input type="checkbox"> ${description}</input>`;

        const optionalInput = configCell.firstChild;
        optionalInput.checked = true;
        optionalInput.onchange = (e) => callback(e.target.checked, this.sampleEditor);
        
        this.namingOptions.set(namingOption, {
            getter : () => { return optionalInput.checked; },
            setter : (value) => { optionalInput.checked = value ; },
        });
    }

    setEnabled(state) {
        for(const s in this.settings)
            this.settings[s].configRowDOM.cells[1].firstChild.disabled = !state;
    }

    optionalFormatState() {
        let state = 0;
        for(let i = 0; i < this.optionalFormats.length; i++) {
            if (this.optionalFormats[i].checked)
                state = state | (1 << i);
        }

        return state;
    }

    toTransportObject() {
        let namingOptionsValue = 0;        
        this.namingOptions.forEach((prop, key) => { if (prop.getter()) namingOptionsValue = namingOptionsValue | key; });

        return {
            elementKindPrefixes: this.settings.map((e) => {
                return {elementKind: e.elementKind, prefix: e.prefix}
            }),
            namingOptions: namingOptionsValue
        };
    }

    loadFromJSON(json) {
        const settingsData = JSON.parse(json);
        this.namingOptions.forEach((prop, key) => {
            prop.setter((settingsData.namingOptions & key) === key);
        });
        
        settingsData.elementKindPrefixes.forEach( (entry, index) => { 
            for(const i in this.settings) {
                if (this.settings[i].elementKind === entry.elementKind) {
                    this.settings[i].updateValue(entry.prefix); 
                }
            }
        });
    }

    isEnabled(option) {
        return this.namingOptions.get(option).getter();
    }
}

class OptionalMemberNameFormat {
    constructor(settingsManager, groupIndex, checkbox, valueExtractor) {
        this.settingsManager = settingsManager;
        this.groupIndex = groupIndex;
        this.valueExtractor = valueExtractor;
        this.checkbox = checkbox;
        this.modifiers = [];
    }

    addConditionalModifier(namingOptions, description, checked, callback) {
        const optionalModifier = new OptionalMemberNameModifier(this.settingsManager.settingsTable, description, checked);
        this.modifiers.push(optionalModifier);

        this.checkbox.addEventListener('change', function(e) {
            optionalModifier.checkbox.disabled = !this.checked;
        });

        optionalModifier.checkbox.onchange = () => {
            this.settingsManager.updateConditionalFormat(this.groupIndex, true, this.extractValue.bind(this));
        };

        const temp = this.valueExtractor;
        this.valueExtractor = (setting) => {
            const value = temp(setting);
            return callback(value, optionalModifier.checkbox.checked);
        };

        this.settingsManager.namingOptions.set(namingOptions, 
            { 
                getter : () => { return optionalModifier.checkbox.checked; },
                setter : (value) => { optionalModifier.checkbox.checked = value ; },
            });

        return optionalModifier;
    }

    extractValue(setting) { 
        return this.valueExtractor(setting); 
    }
}

class OptionalMemberNameModifier {
    constructor(settingsTable, description, checked) {
        const x = settingsTable.insertRow(-1);
        const modifierCell = x.insertCell(0);
        modifierCell.setAttribute("colspan", "2");

        modifierCell.innerHTML = `<input type="checkbox" ${checked ? 'checked' : ''}> ${description}</input>`;
        this.checkbox = modifierCell.firstChild;
    }
}

class Setting {
    constructor(elementKind, start, name, placeholder, memberName, prefix) {
        this.start = start;
        this.memberName = memberName;
        this.name = name;
        this.placeholder = placeholder;
        this.enabled = true;
        this.configRowDOM = null;
        this.elementKind = elementKind;
        this.prefix = prefix;
    }

    setInfo(manager, formatPosition) {
        this.manager = manager;
        this.formatPosition = formatPosition;
    }

    setDOMOwner(configRowDOM) {
        this.configRowDOM = configRowDOM;
    }

    updateExample(newValue) {
        if (this.enabled) {
            const t = this.manager.sampleEditor.getModel().getWordAtPosition({lineNumber: this.start.line, column: this.start.ch + 1});
            const separatorPos = t.word.indexOf("_");
            if (separatorPos !== -1) {
                this.manager.sampleEditor.executeEdits("update-sample", [{forceMoveMarkers : false, range: new monaco.Range(this.start.line, t.startColumn, this.start.line, t.startColumn + separatorPos), text:newValue }]);
            }
        }
        this.prefix = newValue;
    }

    updateValue(newValue) {
        this.updateExample(newValue);
        this.configRowDOM.cells[1].firstChild.value = newValue;
    }

    updateConditionalFormat(groupIndex, replace, optionalFormats, valueExtractor) {
        const t = this.manager.sampleEditor.getModel().getWordAtPosition({lineNumber: this.start.line, column: this.start.ch});
        
        let start = -1;
        let firstEnabled = -1;
        let lastEnabled = -1;
        for(let i = 0; i < optionalFormats.length; i++)
        {
            if (optionalFormats[i].checked && i !== groupIndex) {
                lastEnabled = i;
                if (firstEnabled === -1)
                    firstEnabled = i;

                if (i < groupIndex)
                    start = t.word.indexOf("_", start + 1);
            }
        }

        let newValue = "";
        let end = 0;

        if (optionalFormats[groupIndex].checked) {
            newValue = valueExtractor(this);

            if (groupIndex > lastEnabled) {
                newValue = "_" + newValue;
                if (replace){
                    end = t.word.length - start;
                    start = start - 1;
                }
                else {
                    start = t.word.length - 1;
                }
            }
            else {
                newValue = newValue + "_";
                if (groupIndex < firstEnabled) {
                    start = -1;
                }
                
                if (replace) {
                    end = this.calculateEnd(t, start);
                }
            }
        }
        else {
            end = this.calculateEnd(t, start);
            if (start !== -1)
               start = start - 1;
        }

        this.manager.sampleEditor.executeEdits("update-conditional-option", [{forceMoveMarkers : false, range: new monaco.Range(this.start.line, t.startColumn + start + 1, this.start.line, t.startColumn + start + 1 + end), text:newValue }]);        
    }

    calculateEnd(t, start) {
        let end = t.word.indexOf("_", start + 1); // assumes there are at least one optional formating following the one specified by *pos* that is enabled.
        if (end === -1) // if theres none use the length of the string.
            end = t.word.length;
        
        return (end - start);            
    }

    toggleGutter() {
        let decorations;

        decorations = this.manager.sampleEditor.deltaDecorations([], [
        {
            range: new monaco.Range(this.start.line, 1, this.start.line, 1),
                options: {
                    isWholeLine: true,
                    className: 'current_settings_class',
                    glyphMarginClassName: 'line_arrow'
                }
        }]);
            
        this.configRowDOM.onmouseout = () => {
            if (decorations) {
                this.manager.sampleEditor.deltaDecorations(decorations, []);
                decorations = null;
            }
        };
    }
}

function hideSettings() {
    document.getElementById("settingsDiv").style.width = "0";

    if (settings.isEnabled(NamingOptions.StoreSettingsLocally)) {
        setCookie("cecilifier-settings", JSON.stringify(settings.toTransportObject()), 1000);
    } else {
        deleteCookie("cecilifier-settings");
    }    
}

function changeCecilifierSettings() {
    document.getElementById("settingsDiv").style.width = "100%";
}