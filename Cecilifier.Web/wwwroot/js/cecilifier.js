let websocket;
let cecilifiedCode;
let settings;
let csharpCode;

class CecilifierRequest
{
    constructor(code, options, settings) {
        this.code = code;
        this.options = options;
        this.settings = settings;
    }
}

class WebOptions
{
    constructor(deployKind) {
        this.deployKind = deployKind;
    }
}

function initializeSite(errorAccessingGist, gist, version) {
    require.config({ paths: { vs: 'lib/node_modules/monaco-editor/min/vs' } });

    require(['vs/editor/editor.main'], function () {
        csharpCode = monaco.editor.create(document.getElementById('csharpcode'), {
            theme: "vs-dark",
            value: ['using System;', 'class Foo', '{', '\tvoid Bar() => Console.WriteLine("Hello World!");', '}'].join('\n'),
            language: 'csharp',
            minimap: { enabled: false },
            fontSize: 16,
            glyphMargin: true,
        });
        
        cecilifiedCode = monaco.editor.create(document.getElementById('cecilifiedcode'), {
            theme: "vs-dark",
            value: "",
            language: 'csharp',
            readOnly: true,
            minimap: { enabled: false },
            fontSize: 16,
            glyphMargin: true,
         });
         
        // Configure keyboard shortcuts
        csharpCode.addCommand(monaco.KeyMod.CtrlCmd  | monaco.KeyMod.Alt | monaco.KeyCode.KEY_D, function() {
            simulateClick("downloadProject");
        });

        csharpCode.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyMod.Alt | monaco.KeyCode.KEY_C, function() {
            simulateClick("sendbutton");
        });

        csharpCode.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyMod.Alt | monaco.KeyCode.KEY_S, function() {
            changeCecilifierSettings();
        });

        csharpCode.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.US_OPEN_SQUARE_BRACKET , function() {
            const options = csharpCode.getRawOptions();
            const newFontSize = Math.ceil(options.fontSize - options.fontSize * 0.05);

            csharpCode.updateOptions({ fontSize: newFontSize });
        });

        csharpCode.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.US_CLOSE_SQUARE_BRACKET , function() {
            const options = csharpCode.getRawOptions();

            const newFontSize = Math.ceil(options.fontSize * 1.05);
            csharpCode.updateOptions({ fontSize: newFontSize });
        });

        window.onresize = function(ev) {
            updateEditorsSize();
        }

        updateEditorsSize();

        initializeFormattingSettings();
        setSendToDiscordTooltip();
        
        handleGist(gist, errorAccessingGist);
    });
    
    setTooltips(version);
    initializeWebSocket();
    disableScroll();
}

function initializeFormattingSettings() {
    const formatSettingsExampleCode = `/*[Obsolete] class AClass<T>
{
    public int field;
    public event Action AnEvent;
    public string Property { get { return "P"; } }
    public void Method() {}
}

struct AStruct { }
enum AnEnum { }
interface Interface {}
delegate void ADelegate(int i);
*/
//ClassDeclaration : AClass
var cls_AClass_0 = new TypeDefinition("", "AClass\`1", TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.NotPublic, assembly.MainModule.TypeSystem.Object);

var gp_T_1 = new Mono.Cecil.GenericParameter("T", cls_AClass_0) ;

var attr_Obsolete_2 = new CustomAttribute(assembly.MainModule.ImportReference(typeof(System.ObsoleteAttribute).GetConstructor(new Type[0] {  })));

var fld_field_3 = new FieldDefinition("field", FieldAttributes.Public, assembly.MainModule.TypeSystem.Int32);

var git_AClass_8 = cls_0.MakeGenericInstanceType(cls_AClass_0.GenericParameters.ToArray());

var evt_AnEvent_13 = new EventDefinition("AnEvent", EventAttributes.None, assembly.MainModule.ImportReference(typeof(Action)));
    
var prop_Property_14 = new PropertyDefinition("Property", PropertyAttributes.None, assembly.MainModule.TypeSystem.String);

var md_Method_19 = new MethodDefinition("Method", MethodAttributes.Public | MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Void);

var p_value_21 = new ParameterDefinition("value", ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32);

var il_addAnEvent_7 = md_Method_19.Body.GetILProcessor();

var ctor_AClass_22 = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.RTSpecialName | MethodAttributes.SpecialName, assembly.MainModule.TypeSystem.Void);

var Call_25 = il_23.Create(OpCodes.Call, assembly.MainModule.ImportReference(TypeHelpers.DefaultCtorFor(cls_0.BaseType)));

var s_AStruct_27 = new TypeDefinition("", "AStruct", TypeAttributes.SequentialLayout | TypeAttributes.Sealed |TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.NotPublic, assembly.MainModule.TypeSystem.Object);

var e_AnEnum_28 = new TypeDefinition("", "AnEnum", TypeAttributes.Private | TypeAttributes.Sealed, assembly.MainModule.ImportReference(typeof(System.Enum)));

var itf_Interface_27 = new TypeDefinition("", "Interface", TypeAttributes.Interface | TypeAttributes.Abstract | TypeAttributes.NotPublic);

var del_ADelegate_30 = new TypeDefinition("", "ADelegate", TypeAttributes.Sealed | TypeAttributes.Private, assembly.MainModule.ImportReference(typeof(System.MulticastDelegate))) { IsAnsiClass = true };

var lv_i_4 = new VariableDefinition(assembly.MainModule.TypeSystem.Int32);

var mr_Foo_5 = new MethodReference("Foo");`;
    
    let formattingSettingsSample = monaco.editor.create(document.getElementById('_formattingSettingsSample'), {
        theme: "vs-dark",
        value: formatSettingsExampleCode,
        language: 'csharp',
        readOnly: false,
        minimap: { enabled: false },
        fontSize: 16,
        glyphMargin: true,
        scrollbar : {
            vertical: "hidden"
        }        
    });

    let sampleDiv = document.getElementById("_formattingSettingsSample");
    let h =  window.innerHeight * 0.90;
    let w =  window.innerWidth * 0.7;
    sampleDiv.style.height = `${h}px`;
    sampleDiv.style.width = `${w}px`;

    formattingSettingsSample.layout();

    initializeSettings(formattingSettingsSample);
}

function disableScroll() {
    // Get the current page scroll position
    const scrollTop = window.pageYOffset || document.documentElement.scrollTop;
    const scrollLeft = window.pageXOffset || document.documentElement.scrollLeft;
  
    // if any scroll is attempted, set this to the previous value
    window.onscroll = function() {
        window.scrollTo(scrollLeft, scrollTop);
    };
}

function updateEditorsSize() {
    let csharpCodeDiv = document.getElementById("csharpcode");
    let h = window.innerHeight * 0.35;
    csharpCodeDiv.style.height = `${h}px`;

    var cecilifiedCodeDiv = document.getElementById("cecilifiedcode");
    cecilifiedCodeDiv.style.height = `${window.innerHeight - h - 80}px`;

    csharpCode.layout();
    cecilifiedCode.layout();
}

function setTooltips(version) {
    let msg = `Cecilifier version ${version}<br/><br/>Cecilifier is meant to make it easier to learn how to use <a href="https://github.com/jbevain/cecil" target="_blank">Mono.Cecil</a>. You can read more details about it in its <a href="https://programing-fun.blogspot.com/2019/02/making-it-easier-to-getting-started.html" target="_blank">blog announcement</a>.`;
    
    let defaultDelay =  [500, null];
    tippy('#aboutSpan2', {
        content: msg,
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });
    
    tippy('#csharpcode-container', {
        content: "Type any valid C# code to generate the equivalent Cecil api calls.",
        placement: 'bottom',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#cecilifiedcode-container', {
        content: "After pressing <i>Cecilify your code!</i> button, the generate code will be show here.",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#sendbutton', {
        content: "After entering the code you want to process, press this button. (Ctrl-Alt-C)",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#downloadProject', {
        content: "Use this option if you want to download a .Net Core 3.0 project, ready for you to play with! (Ctrl-Alt-D)",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#copyToClipboard', {
        content: "Copy cecilified code to clipboard.",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#changeSettings', {
        content: "Change various Cecilifier options.<br/><br/>Use this to configure how variables are named in the cecilified code. (Ctrl-Alt-S)",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#keyboard-shortcuts', {
        content: "<div style='text-align:left'>\
                  <p><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">Alt</kbd> + <kbd class=\"kbc-button\">C</kbd> Cecilify the code.</p>\
                  <p><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">Alt</kbd> + <kbd class=\"kbc-button\">D</kbd> Downloads project with cecilified code.</p>\
                  <p><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">Alt</kbd> + <kbd class=\"kbc-button\">S</kbd> Opens settings page.</p>\
                  <p><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">]</kbd> Increases font size.</p>\
                  <p><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">[</kbd> Decreases font size.</p>\
                  </div>",
        placement: 'top',
        interactive: false,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });    
}

function initializeSettings(formattingSettingsSample) {
    
    const startLine = 15;
    
    settings = new SettingsManager(formattingSettingsSample, [
        new Setting(ElementKind.Class, {line: startLine, ch: 5}, "Class", "prefix to be used for classes", "AClass", "cls"),
        new Setting(ElementKind.GenericParameter, {line: startLine + 2, ch: 5}, "Generic Parameter", "generic parameters prefix","T", "gp"),
        new Setting(ElementKind.Attribute, {line: startLine + 4, ch: 5}, "Attribute", "attribute prefix","Obsolete", "attr"),       
        new Setting(ElementKind.Field, {line: startLine + 6, ch: 5}, "Field", "field prefix","field", "fld"),
        new Setting(ElementKind.GenericInstance, {line: startLine + 8, ch: 5}, "Generic Instance", "generic instance prefix","AClass", "gi"),
        new Setting(ElementKind.Event, {line: startLine + 10, ch: 5}, "Event", "event declaration prefix","AnEvent", "evt"),    
        new Setting(ElementKind.Property, {line: startLine + 12, ch: 5}, "Property", "property prefix","Property", "prop"),
        new Setting(ElementKind.Method, {line: startLine + 14, ch: 5}, "Method", "method prefix","Method", "m"),
        new Setting(ElementKind.Parameter, {line: startLine + 16, ch: 5}, "Parameter", "parameter prefix","value", "p"),
        new Setting(ElementKind.IL, {line: startLine + 18, ch: 5}, "IL", "il variable prefix","addAnEvent", "il"),
        new Setting(ElementKind.Constructor, {line: startLine + 20, ch: 5}, "Constructor", "constructor prefix","AClass", "ctor"),
        new Setting(ElementKind.Struct, {line: startLine + 24, ch: 5}, "Struct", "struct prefix","AStruct", "st"),
        new Setting(ElementKind.Enum, {line: startLine + 26, ch: 5}, "Enum", "enum prefix","AnEnum", "e"),
        new Setting(ElementKind.Interface, {line: startLine + 28, ch: 5}, "Interface", "interface prefix","Interface", "itf"),
        new Setting(ElementKind.Delegate, {line: startLine + 30, ch: 5}, "Delegate", "delegate prefix","ADelegate", "del"),
        new Setting(ElementKind.LocalVariable, {line: startLine + 32, ch: 5}, "Local Variable", "local variable prefix","i", "lv"),
        new Setting(ElementKind.MemberReference, {line: startLine + 34, ch: 5}, "Member Reference", "Member reference prefix","i", "mr"),
        new Setting(ElementKind.Label, {line: startLine + 36, ch: 5}, "Jump Label", "Jump Label Prefix","jump", "lbl"),
    ]);

    settings.validateOptionalFormat = () => {
        const sel = settings.optionalFormatState();

        if ((sel & 0x7) === 0x4) {
            return "Unique id cannot be the only format.";
        }
    
        if  (sel === 0) {
            return "At least one format need to be included in variable names.";
        }
    
        return null;
    };
     
    settings.initialize(document.getElementById("cecilifierSettings"));
    settings.addConditionalFormat(
        NamingOptions.PrefixVariableNamesWithElementKind, 
        "Prefix variable name with element kind",
        0, 
        "Variable names will have the related element kind (class, struct, enum, etc) appended.", 
        (setting) => setting.prefix, 
        (enabled) => settings.setEnabled(enabled));

    const conditionalMemberName = settings.addConditionalFormat(
        NamingOptions.AppendElementNameToVariables,
        "Append member name to variable",
        1,
        "Use this option to append the related element name (the name of the class, struct, property, etc) to variable names.",
        (setting) => setting.memberName,
        (enabled) => {
        });

    conditionalMemberName.addConditionalModifier(
        NamingOptions.CamelCaseElementNames, 
        "Camel case variable names", 
        false, 
        (component, enabled) => enabled ? component[0].toLowerCase() + component.substring(1) : component);
         
    settings.addConditionalFormat(
        NamingOptions.SuffixVariableNamesWithUniqueId, 
        "Suffix variable names with unique Id", 
        2, 
        "Use this option to avoid variable names clashes. If the cecilified code is simple enough you may disable this.", 
        (function(setting) { var id = 1; return function() { id++; return id; } } )(), 
        (enabled) => { });

    settings.addBooleanOption(
        NamingOptions.PrefixInstructionsWithILOpCodeName, 
        "Append IL opcode to instruction variables", 
        "Use this option to make it easier to reason about the cecilified code.", 
        (enabled, sampleEditor) => {
            const lineNumber = 37;
            const wap = sampleEditor.getModel().getWordAtPosition({lineNumber: lineNumber, column: 5});
            const newValue = enabled ? "Call" : "inst";
            const separatorPos = wap.word.indexOf("_");
            sampleEditor.executeEdits("toggle-il", [{forceMoveMarkers : false, range: new monaco.Range(lineNumber, wap.startColumn, lineNumber, wap.startColumn + separatorPos), text:newValue }])
        });
     
    settings.addBooleanOption(
        NamingOptions.AddCommentsToMemberDeclarations, 
        "Add comments before type/member declarations", 
        "Such comments may help in correlating the cecilified to the original source.", 
        (enabled, sampleEditor) => {
            const newValue = enabled ? "//ClassDeclaration : AClass" : "";
            sampleEditor.executeEdits("", [{forceMoveMarkers : false, range: new monaco.Range(startLine - 1, 1, startLine - 1, 64), text:newValue }]);
        });

    settings.addBooleanOption(
        NamingOptions.StoreSettingsLocally, 
        "Store settings locally (cookies)", 
        "Enabling this option so Cecilifier will remember your settings.", 
        (setting, sampleEditor) => { });
    
    settings.addBooleanOption(
        NamingOptions.IncludeSourceInErrorReports, 
        "Include source code when reporting failures to developer discord channel", 
        "Enable this to send the code being cecilified to developer (private) discord channel (if disabled only the error message is sent).\nNote that no matter the state of this option messages are sent anonymously.", 
        (setting, sampleEditor) => { });

    const storedSettings = getCookie("cecilifier-settings");
    if (storedSettings.length > 0) {
        settings.loadFromJSON(storedSettings);
    }
}

function setSendToDiscordTooltip()
{
    let msgBody = "the source from the top textbox will be sent to an internal discord channel (accessible only to Cecilifier's author)";
    let msg = null;
    if (getSendToDiscordValue())
    {
        msg = `Publish On: ${msgBody}. Preferable if the contents of the code is not sensitive. This helps Cecilifier developer to better understand usage pattern.`;
    }
    else
    {
        msg = "Publish Off: " + msgBody +  " only in case of errors.";
    }
}

function setAlert(div_id, msg) {
    const div = document.getElementById(div_id);

    if (msg == null) {
        div.style.opacity = "0";
        div.style.position = "absolute";
        div.children[1].innerHTML = "";
    } else {
        div.style.opacity = "1";
        div.style.position = "relative";
        div.children[1].innerHTML = msg;
        div.style.display = "block";
    }
}

function clearError() {
    setAlert("cecilifier_error", null);
}

function setError(str) {
    setAlert("cecilifier_error", str);
}

function initializeWebSocket() {
    const scheme = document.location.protocol === "https:" ? "wss" : "ws";
    const port = document.location.port ? (":" + document.location.port) : "";
    const connectionURL = scheme + "://" + document.location.hostname + port + "/ws";

    websocket = new WebSocket(connectionURL);

    const sendButton = document.getElementById("sendbutton");
    sendButton.onclick = function() {
        send(websocket, 'C');
    };

    const downloadProjectButton = document.getElementById("downloadProject");
    downloadProjectButton.onclick = function() {
        send(websocket, 'Z');
    };

    websocket.onopen = function (event) {
    };

    websocket.onclose = function (event) {
    };

    websocket.onerror = function(event) {
        console.error("WebSocket error observed:", event);
    };
    
    websocket.onmessage = function (event) {
        // this is where we get the cecilified code back...
        var response = JSON.parse(event.data);
        if (response.status === 0) {
            var cecilifiedCounter = document.getElementById('cecilified_counter');
            cecilifiedCounter.innerText = response.counter;

            if (response.kind === 'Z') {
                setTimeout(function() {
                    var buttonId = createProjectZip(base64ToArrayBuffer(response.cecilifiedCode), response.mainTypeName + ".zip", 'application/zip');
                    simulateClick(buttonId);
                });
            }
            else {
                cecilifiedCode.setValue(response.cecilifiedCode);
            }
        } else if (response.status === 1) {
            setError(response.syntaxError.replace(/\n/g, "<br/>"));
        } else if (response.status === 2) {
            setError("Something went wrong. Please report the following error in the google group or in the git repository:\n" + response.error);
        }
    };
}

function getSendToDiscordValue() { return settings.isEnabled(NamingOptions.IncludeSourceInErrorReports); }

function send(websocket, format) {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) {
        alert("socket not connected");
        return;
    }
    clearError();

    const request = new CecilifierRequest(
        csharpCode.getValue(),
        new WebOptions(format),
        settings.toTransportObject());

    websocket.send(JSON.stringify(request));
}

function createProjectZip(text, name, type) {
    const buttonId = "dlbtn";
    const dlbtn = document.getElementById(buttonId);
    const file = new Blob([text], {type: type});
    dlbtn.href = URL.createObjectURL(file);
    dlbtn.download = name;
    
    return buttonId;
}

function base64ToArrayBuffer(base64) {
    const binary_string = window.atob(base64);
    const len = binary_string.length;
    const bytes = new Uint8Array(len);
    for (let i = 0; i < len; i++) {
        bytes[i] = binary_string.charCodeAt(i);
    }
    return bytes.buffer;
}

function simulateClick(elementId) {
    const event = new MouseEvent('click', {
        view: window,
        bubbles: true,
        cancelable: true
    });
    const cb = document.getElementById(elementId);
    const cancelled = !cb.dispatchEvent(event);
}

function copyToClipboard(elementId) {
    navigator.clipboard.writeText(cecilifiedCode.getValue("\r\n"));
}

function cecilifyFromGist(counter) {
    if (websocket.readyState !== WebSocket.OPEN) {
        if (counter < 4) {
            setTimeout(cecilifyFromGist, 500, counter + 1);
        }
    }
    else {
        simulateClick("sendbutton");
    }
}

function handleGist(gist, errorAccessingGist) {
    if (errorAccessingGist.length === 0) {
        setValueFromGist(gist);
    } else {
        setError(errorAccessingGist);
    }
}

function setValueFromGist(snippet) {
    if (snippet === null || snippet.length === 0)
        return;

    csharpCode.setValue(
            snippet
            .replace(/&quot;/g, '"')
            .replace(/&gt;/g, '>')
            .replace(/&lt;/g, '<')
            .replace(/&#x27;/g, "'")
            .replace(/&#x2B;/g, '+')
            .replace(/&#x38;/g, '&')
            .replace(/&amp;/g, '&'));

    cecilifyFromGist(1);
}
