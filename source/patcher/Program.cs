using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;
if(args.Length!=4)throw new ArgumentException("Patcher original-Assembly-CSharp.dll helper.dll output-Assembly-CSharp.dll game-Managed-folder");
var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(Path.GetDirectoryName(args[0])!);resolver.AddSearchDirectory(Path.GetDirectoryName(args[1])!);
resolver.AddSearchDirectory(args[3]);
using var assembly=AssemblyDefinition.ReadAssembly(args[0],new ReaderParameters{AssemblyResolver=resolver,InMemory=true});
using var helper=AssemblyDefinition.ReadAssembly(args[1],new ReaderParameters{AssemblyResolver=resolver,InMemory=true});
var module=assembly.MainModule;
if(module.AssemblyReferences.Any(r=>r.Name=="Torment.VerticalDialogue"))throw new InvalidOperationException("Input already patched; use the verified original backup");
var layout=helper.MainModule.Types.Single(t=>t.FullName=="Torment.VerticalDialogue.Layout");
var apply=module.ImportReference(layout.Methods.Single(m=>m.Name=="Apply"));
var cameraOffset=module.ImportReference(layout.Methods.Single(m=>m.Name=="CameraOffset"));
var window=module.Types.Single(t=>t.FullName=="inXile.UI.TTON.ConversationWindow");
var windowAwake=window.Methods.Single(m=>m.Name=="Awake");
windowAwake.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldc_R4&&i.Operand is float f&&f==-460f).Operand=-320f;
windowAwake.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldc_R4&&i.Operand is float f&&f==440f).Operand=320f;
var late=window.Methods.Single(m=>m.Name=="LateUpdate");int returns=0;
var windowUpdate=window.Methods.Single(m=>m.Name=="Update");
var updateIl=windowUpdate.Body.GetILProcessor();var updateFirst=windowUpdate.Body.Instructions[0];
updateIl.InsertBefore(updateFirst,Instruction.Create(OpCodes.Ldarg_0));
updateIl.InsertBefore(updateFirst,Instruction.Create(OpCodes.Call,module.ImportReference(layout.Methods.Single(m=>m.Name=="BeginContinueShortcutFrame"))));
var continueChecks=windowUpdate.Body.Instructions.Where(i=>i.Operand is MethodReference mr&&mr.Name=="GetControlUpIgnoreHandle"&&i.Previous?.Previous?.OpCode==OpCodes.Ldc_I4_S&&Convert.ToInt32(i.Previous.Previous.Operand)==26).ToArray();
if(continueChecks.Length!=2)throw new InvalidDataException("Unexpected Continue shortcut input sites");
foreach(var input in continueChecks){
 var self=Instruction.Create(OpCodes.Ldarg_0);updateIl.InsertAfter(input,self);
 updateIl.InsertAfter(self,Instruction.Create(OpCodes.Call,module.ImportReference(layout.Methods.Single(m=>m.Name=="ContinueShortcut"))));
}
foreach(var instruction in late.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray()){
 instruction.OpCode=OpCodes.Ldarg_0;instruction.Operand=null;var call=Instruction.Create(OpCodes.Call,apply);late.Body.GetILProcessor().InsertAfter(instruction,call);late.Body.GetILProcessor().InsertAfter(call,Instruction.Create(OpCodes.Ret));returns++;
}
late.Body.MaxStackSize=Math.Max(late.Body.MaxStackSize,1);
// New calls may push an existing short branch beyond its signed-byte range.
var allOpcodes=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Name);
foreach(var instruction in windowUpdate.Body.Instructions.Where(i=>i.OpCode.OperandType==OperandType.ShortInlineBrTarget))instruction.OpCode=allOpcodes[instruction.OpCode.Name[..^2]];
foreach(var instruction in late.Body.Instructions.Where(i=>i.OpCode.OperandType==OperandType.ShortInlineBrTarget))instruction.OpCode=allOpcodes[instruction.OpCode.Name[..^2]];
var offset=window.Methods.Single(m=>m.Name=="GetHandleOffset");offset.Body=new MethodBody(offset);offset.Body.Instructions.Add(Instruction.Create(OpCodes.Call,cameraOffset));offset.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
var norm=window.Methods.Single(m=>m.Name=="GetNormalizedViewScale");norm.Body=new MethodBody(norm);norm.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R4,0f));norm.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
var camera=module.Types.Single(t=>t.Name=="ConversationCameraController");var ctor=camera.Methods.Single(m=>m.Name==".ctor");
var down=ctor.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldc_R4&&i.Operand is float f&&f==-1f);down.Operand=0f;
// The original printer pads short blocks up to the viewport height. Remove that
// padding, retain chronological ordering, and let native ScrollToAnimator follow
// the end of the conversation rather than its newest block's first line.
var printer=module.Types.Single(t=>t.Name=="ConversationNodePrinter");
var responseLinks=printer.Methods.Single(m=>m.Name=="AddAnyPlayerResponseLinks");
var responseFormat=responseLinks.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldstr&&i.Operand is string s&&s.StartsWith("[padding=5,20,0,100]"));
responseFormat.Operand=((string)responseFormat.Operand).Replace("[padding=5,20,0,100]","[padding=5,20,0,56]");
var removeResponses=printer.Methods.Single(m=>m.Name=="RemovePlayerResponseLinks");
removeResponses.Body=new MethodBody(removeResponses);
removeResponses.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
removeResponses.Body.Instructions.Add(Instruction.Create(OpCodes.Call,module.ImportReference(layout.Methods.Single(m=>m.Name=="RemoveResponsesFirst"))));
removeResponses.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
var buffer=printer.Methods.Single(m=>m.Name=="RecalculateBuffer");
buffer.Body=new MethodBody(buffer);buffer.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
var changed=printer.Methods.Single(m=>m.Name=="ConversationNodeChanged");
var partialBranch=changed.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ble_Un);
partialBranch.OpCode=OpCodes.Ble_Un; // Always take the existing bottom-scroll path.
var ci=changed.Body.GetILProcessor();
var jump=Instruction.Create(OpCodes.Br,(Instruction)partialBranch.Operand);
ci.InsertAfter(partialBranch,jump);
foreach(var instruction in changed.Body.Instructions.Where(i=>i.OpCode.OperandType==OperandType.ShortInlineBrTarget))instruction.OpCode=allOpcodes[instruction.OpCode.Name[..^2]];
var wheel=module.Types.Single(t=>t.Name=="StringWheel");
var paint=wheel.Methods.Single(m=>m.Name=="Repaint"&&m.Parameters.Count==1);
var paintIl=paint.Body.GetILProcessor();var start=paint.Body.Instructions[0];
var adjust=module.ImportReference(layout.Methods.Single(m=>m.Name=="BottomPaintOffset"));
foreach(var ins in new[]{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,wheel.Fields.Single(f=>f.Name=="_totalHeight")),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,wheel.Methods.Single(m=>m.Name=="get_BaseHeight")),Instruction.Create(OpCodes.Call,adjust),Instruction.Create(OpCodes.Starg,paint.Parameters[0])})paintIl.InsertBefore(start,ins);
paint.Body.MaxStackSize=Math.Max(paint.Body.MaxStackSize,5);
// The helper owns visible scrolling. Complete only the conversation's native
// ScrollToAnimator immediately so its invisible second animation cannot hold input.
var scrollAnimator=module.Types.Single(t=>t.Name=="ScrollToAnimator");
var scrollProcess=scrollAnimator.Methods.Single(m=>m.Name=="Process");
var scrollIl=scrollProcess.Body.GetILProcessor();var scrollStart=scrollProcess.Body.Instructions[0];
var parentField=module.Types.Single(t=>t.Name=="BaseAnimator").Fields.Single(f=>f.Name=="_parent");
foreach(var ins in new[]{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,parentField),Instruction.Create(OpCodes.Call,module.ImportReference(layout.Methods.Single(m=>m.Name=="UseVisualDialogueScroll"))),Instruction.Create(OpCodes.Brfalse,scrollStart),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldc_I4_1),Instruction.Create(OpCodes.Stfld,scrollAnimator.Fields.Single(f=>f.Name=="Instant"))})scrollIl.InsertBefore(scrollStart,ins);
// Port the archive's useful behaviours onto the Steam build. Do not transplant
// its SocialManager/SteamManager replacements or recompiled Conversation class.
var conversation=module.Types.Single(t=>t.Name=="Conversation");
var getText=conversation.Methods.Single(m=>m.Name=="GetNodeText");
var colour=module.ImportReference(layout.Methods.Single(m=>m.Name=="ModDialogueColours"));
foreach(var ret in getText.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray()){
 ret.OpCode=OpCodes.Call;ret.Operand=colour;getText.Body.GetILProcessor().InsertAfter(ret,Instruction.Create(OpCodes.Ret));
}
foreach(var instruction in getText.Body.Instructions.Where(i=>i.OpCode.OperandType==OperandType.ShortInlineBrTarget))instruction.OpCode=allOpcodes[instruction.OpCode.Name[..^2]];
var addDialogue=printer.Methods.Single(m=>m.Name=="AddDialogueText");
var concat=addDialogue.Body.Instructions.First(i=>i.OpCode==OpCodes.Call&&i.Operand is MethodReference mr&&mr.Name=="Concat");
addDialogue.Body.GetILProcessor().InsertAfter(concat,Instruction.Create(OpCodes.Call,module.ImportReference(layout.Methods.Single(m=>m.Name=="ModSkillColours"))));
foreach(var instruction in addDialogue.Body.Instructions.Where(i=>i.OpCode.OperandType==OperandType.ShortInlineBrTarget))instruction.OpCode=allOpcodes[instruction.OpCode.Name[..^2]];
var indentText=module.ImportReference(layout.Methods.Single(m=>m.Name=="ApplySpeechIndent"));
foreach(var method in printer.Methods.Where(m=>m.Name=="AddDialogueText"||m.Name=="AddPlayerText")){
 var il=method.Body.GetILProcessor();
 foreach(var ins in method.Body.Instructions.Where(i=>i.Operand is MethodReference mr&&mr.Name=="Add"&&mr.DeclaringType.Name=="StringWheel"&&mr.Parameters.Count==1&&mr.Parameters[0].ParameterType.FullName=="System.String").ToArray())il.InsertBefore(ins,Instruction.Create(OpCodes.Call,indentText));
 foreach(var instruction in method.Body.Instructions.Where(i=>i.OpCode.OperandType==OperandType.ShortInlineBrTarget))instruction.OpCode=allOpcodes[instruction.OpCode.Name[..^2]];
}
var move=module.Types.Single(t=>t.Name=="CameraControl").Methods.Single(m=>m.Name=="get_CameraMoveDelta");
move.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldc_R4).Operand=0.1f;
// Keep the original Animator speed: frame glow alpha/fill curves share it.
var itemDisplay=module.Types.Single(t=>t.Name=="ConversationItemGainedDisplay");
foreach(var fieldName in new[]{"deployedPosition","hiddenPosition"})
 if(itemDisplay.Fields.Single(f=>f.Name==fieldName).FieldType.FullName!="UnityEngine.Vector3")throw new InvalidDataException("Unexpected item position type: "+fieldName);
var itemAwake=itemDisplay.Methods.Single(m=>m.Name=="Awake");
var itemRet=itemAwake.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ret);
itemRet.OpCode=OpCodes.Ldarg_0;
var configure=Instruction.Create(OpCodes.Call,module.ImportReference(layout.Methods.Single(m=>m.Name=="ConfigureItemCard")));
itemAwake.Body.GetILProcessor().InsertAfter(itemRet,configure);
itemAwake.Body.GetILProcessor().InsertAfter(configure,Instruction.Create(OpCodes.Ret));
// Native DeployContainer/HideContainer tweens use the adapted slide endpoints.
// Keep the native exploration party HUD in the conversation screen's element set.
// Only its player portrait remains visible; native elements retain their lifecycle.
var conversationScreen=module.Types.Single(t=>t.Name=="ConversationScreenSetting");
var screenElements=conversationScreen.Methods.Single(m=>m.Name=="GetScreenElements");
var originalScreenCalls=screenElements.Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>(MethodReference)i.Operand).ToArray();
var getElement=originalScreenCalls.First(m=>m.Name=="GetElement");
var spawnElement=originalScreenCalls.First(m=>m.Name=="SpawnElement");
var elementType=module.Types.Single(t=>t.FullName=="inXile.UI.Element");
screenElements.Body=new MethodBody(screenElements){InitLocals=true};
var elementArray=new VariableDefinition(new ArrayType(elementType));screenElements.Body.Variables.Add(elementArray);
var se=screenElements.Body.GetILProcessor();
se.Append(Instruction.Create(OpCodes.Ldc_I4_3));se.Append(Instruction.Create(OpCodes.Newarr,elementType));se.Append(Instruction.Create(OpCodes.Stloc,elementArray));
var elementIds=new[]{36,41,12};
for(int i=0;i<elementIds.Length;i++){
 se.Append(Instruction.Create(OpCodes.Ldloc,elementArray));se.Append(Instruction.Create(OpCodes.Ldc_I4,i));
 se.Append(Instruction.Create(OpCodes.Ldc_I4,elementIds[i]));se.Append(Instruction.Create(OpCodes.Call,getElement));se.Append(Instruction.Create(OpCodes.Dup));
 var store=Instruction.Create(OpCodes.Stelem_Ref);se.Append(Instruction.Create(OpCodes.Brtrue,store));se.Append(Instruction.Create(OpCodes.Pop));
 se.Append(Instruction.Create(OpCodes.Ldc_I4,elementIds[i]));se.Append(Instruction.Create(OpCodes.Call,spawnElement));se.Append(store);
}
se.Append(Instruction.Create(OpCodes.Ldloc,elementArray));se.Append(Instruction.Create(OpCodes.Ret));
var partyHudType=module.Types.Single(t=>t.Name=="PartyElement");
var blockPortrait=module.ImportReference(layout.Methods.Single(m=>m.Name=="BlockPortraitAction"));
foreach(var type in module.Types.Where(t=>t.Name=="PortraitElement"||t.Name=="ExplorationPortraitElement")){
 foreach(var method in type.Methods.Where(m=>m.Name=="Click"||m.Name=="SetSelected")){
  var il=method.Body.GetILProcessor();var first=method.Body.Instructions[0];
  var prefix=new List<Instruction>{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,blockPortrait),Instruction.Create(OpCodes.Brfalse,first)};
  if(method.Name=="SetSelected"){
   prefix.Add(Instruction.Create(OpCodes.Ldc_I4_0));prefix.Add(Instruction.Create(OpCodes.Starg,method.Parameters[0]));
  }else prefix.Add(Instruction.Create(OpCodes.Ret));
  foreach(var ins in prefix)il.InsertBefore(first,ins);
 }
}
var useDialogue=module.ImportReference(layout.Methods.Single(m=>m.Name=="UseDialogueControl"));
foreach(var method in partyHudType.Methods.Where(m=>m.Name=="ToggleItemRow"||m.Name=="ToggleOcculticRow")){
 var il=method.Body.GetILProcessor();var first=method.Body.Instructions[0];
 foreach(var ins in new[]{Instruction.Create(OpCodes.Ldc_I4,method.Name=="ToggleItemRow"?0:1),Instruction.Create(OpCodes.Call,useDialogue),Instruction.Create(OpCodes.Brfalse,first),Instruction.Create(OpCodes.Ret)})il.InsertBefore(first,ins);
 foreach(var instruction in method.Body.Instructions.Where(i=>i.OpCode.OperandType==OperandType.ShortInlineBrTarget))instruction.OpCode=allOpcodes[instruction.OpCode.Name[..^2]];
}
var rowType=module.Types.Single(t=>t.Name=="AbilityRow");
var hudActive=module.ImportReference(layout.Methods.Single(m=>m.Name=="DialogueHudActive"));
foreach(var method in rowType.Methods.Where(m=>m.Name=="Update"||m.Name=="ConvertHotKeyToAbilityClick")){
 var il=method.Body.GetILProcessor();var first=method.Body.Instructions[0];
 var skip=method.ReturnType.FullName=="System.Void"?Instruction.Create(OpCodes.Ret):Instruction.Create(OpCodes.Br,method.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldc_I4_S&&Convert.ToInt32(i.Operand)==9));
 foreach(var ins in new[]{Instruction.Create(OpCodes.Call,hudActive),Instruction.Create(OpCodes.Brfalse,first),skip})il.InsertBefore(first,ins);
}
var screens=module.Types.Single(t=>t.Name=="Screens");
var currentScreen=screens.Methods.Single(m=>m.Name=="get_CurrentScreen");
var playerField=module.Types.Single(t=>t.Name=="GameState").Fields.Single(f=>f.Name=="s_playerCharacter");
var syncHud=module.ImportReference(layout.Methods.Single(m=>m.Name=="SyncPlayerHud"));
foreach(var method in partyHudType.Methods.Where(m=>m.Name=="DoShow"||m.Name=="HandleScreenSwitchEnd")){
 var il=method.Body.GetILProcessor();
 foreach(var ret in method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray()){
  ret.OpCode=OpCodes.Ldarg_0;
  var cursorHud=ret;
  foreach(var ins in new[]{Instruction.Create(OpCodes.Ldsfld,playerField),Instruction.Create(OpCodes.Call,currentScreen),Instruction.Create(OpCodes.Ldc_I4_4),Instruction.Create(OpCodes.Ceq),Instruction.Create(OpCodes.Call,syncHud),Instruction.Create(OpCodes.Ret)}){il.InsertAfter(cursorHud,ins);cursorHud=ins;}
 }
 foreach(var instruction in method.Body.Instructions.Where(i=>i.OpCode.OperandType==OperandType.ShortInlineBrTarget))instruction.OpCode=allOpcodes[instruction.OpCode.Name[..^2]];
 method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,4);
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);assembly.Write(args[2]);
using var verification=AssemblyDefinition.ReadAssembly(args[2]);var verified=verification.MainModule.Types.Single(t=>t.FullName==window.FullName);
var calls=verified.Methods.Single(m=>m.Name=="LateUpdate").Body.Instructions.Count(i=>i.Operand is MethodReference mr&&mr.FullName.Contains("Torment.VerticalDialogue.Layout::Apply"));
if(calls!=returns)throw new InvalidDataException("Hook verification failed");
if(verification.MainModule.Types.Count!=module.Types.Count||verification.MainModule.Types.Sum(t=>t.Methods.Count)!=module.Types.Sum(t=>t.Methods.Count))throw new InvalidDataException("Assembly member inventory changed");
if(verification.MainModule.AssemblyReferences.Any(r=>r.Name.StartsWith("System.Runtime")))throw new InvalidDataException("Modern runtime reference leaked into game assembly");
Console.WriteLine(JsonSerializer.Serialize(new{originalSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))),patchedSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[2]))),lateUpdateExitHooks=returns,methodsChanged=new[]{"ConversationWindow.LateUpdate","ConversationWindow.GetHandleOffset","ConversationWindow.GetNormalizedViewScale","ConversationCameraController..ctor","ConversationNodePrinter.RecalculateBuffer","ConversationNodePrinter.ConversationNodeChanged","StringWheel.Repaint(float)"},mvid=verification.MainModule.Mvid}));


