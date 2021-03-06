using System;
using System.Collections.Generic;
using System.Linq;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.LanguageServer;

namespace Deltin.Deltinteger.Parse
{
    public class DefinedMethod : DefinedFunction
    {
        private readonly DeltinScriptParser.Define_methodContext context;
        private MethodAttributeHandler[] attributes;

        // Attributes
        public bool IsSubroutine { get; private set; }
        public string SubroutineName { get; private set; }

        // Block data
        public BlockAction block { get; private set; }

        /// <summary>If there is only one return statement, return the reference to
        /// the return expression instead of assigning it to a variable to reduce the number of actions.</summary>
        public bool multiplePaths;

        public SubroutineInfo subroutineInfo { get; private set; }

        public DefinedMethod(ParseInfo parseInfo, Scope objectScope, Scope staticScope, DeltinScriptParser.Define_methodContext context, CodeType containingType)
            : base(parseInfo, context.name.Text, new Location(parseInfo.Script.Uri, DocRange.GetRange(context.name)))
        {
            this.context = context;
            Attributes.ContainingType = containingType;

            DocRange nameRange = DocRange.GetRange(context.name);

            // Get the attributes.
            GetAttributes();

            SetupScope(Static ? staticScope : objectScope);

            // Get the type.
            if (context.VOID() == null)
            {
                doesReturnValue = true;
                ReturnType = CodeType.GetCodeTypeFromContext(parseInfo, context.code_type());
            }

            // Setup the parameters and parse the block.
            if (!IsSubroutine)
                SetupParameters(context.setParameters(), false);
            else
            {
                Attributes.Parallelable = true;
                parseInfo.TranslateInfo.AddSubroutine(this);

                // Subroutines should not have parameters.
                SetupParameters(context.setParameters(), true);
            }

            // Override attribute.
            if (Attributes.Override)
            {
                IMethod overriding = objectScope.GetMethodOverload(this);

                // No method with the name and parameters found.
                if (overriding == null) parseInfo.Script.Diagnostics.Error("Could not find a method to override.", nameRange);
                else if (!overriding.Attributes.IsOverrideable) parseInfo.Script.Diagnostics.Error("The specified method is not marked as virtual.", nameRange);
                else overriding.Attributes.AddOverride(this);

                if (overriding != null && overriding.DefinedAt != null)
                {
                    // Make the override keyword go to the base method.
                    parseInfo.Script.AddDefinitionLink(
                        attributes.First(at => at.Type == MethodAttributeType.Override).Range,
                        overriding.DefinedAt
                    );
                }
            }

            if (Attributes.IsOverrideable && AccessLevel == AccessLevel.Private)
                parseInfo.Script.Diagnostics.Error("A method marked as virtual or abstract must have the protection level 'public' or 'protected'.", nameRange);

            // Syntax error if the block is missing.
            if (context.block() == null) parseInfo.Script.Diagnostics.Error("Expected block.", nameRange);

            // Add to the scope. Check for conflicts if the method is not overriding.
            objectScope.AddMethod(this, parseInfo.Script.Diagnostics, nameRange, !Attributes.Override);

            // Add the hover info.
            parseInfo.Script.AddHover(DocRange.GetRange(context.name), GetLabel(true));

            if (Attributes.IsOverrideable)
                parseInfo.Script.AddCodeLensRange(new ImplementsCodeLensRange(this, parseInfo.Script, CodeLensSourceType.Function, nameRange));

            parseInfo.TranslateInfo.ApplyBlock(this);
        }

        private void GetAttributes()
        {
            // If the STRINGLITERAL is not null, the method will be stored in a subroutine.
            // Get the name of the rule the method will be stored in.
            if (context.STRINGLITERAL() != null)
            {
                SubroutineName = Extras.RemoveQuotes(context.STRINGLITERAL().GetText());
                IsSubroutine = true;
            }
            
            // method_attributes will ne null if there are no attributes.
            if (context.method_attributes() == null) return;

            int numberOfAttributes = context.method_attributes().Length;
            attributes = new MethodAttributeHandler[numberOfAttributes];

            // Loop through all attributes.
            for (int i = 0; i < numberOfAttributes; i++)
            {
                var newAttribute = new MethodAttributeHandler(context.method_attributes(i));
                attributes[i] = newAttribute;

                bool wasCopy = false;

                // If the attribute already exists, syntax error.
                for (int c = i - 1; c >= 0; c--)
                    if (attributes[c].Type == newAttribute.Type)
                    {
                        newAttribute.Copy(parseInfo.Script.Diagnostics);
                        wasCopy = true;
                        break;
                    }
                
                // Additonal syntax errors. Only throw if the attribute is not a copy.
                if (!wasCopy)
                {
                    // Virtual attribute on a static method (static attribute was first.)
                    if (Static && newAttribute.Type == MethodAttributeType.Virtual)
                        parseInfo.Script.Diagnostics.Error("Static methods cannot be virtual.", newAttribute.Range);
                    
                    // Static attribute on a virtual method (virtual attribute was first.)
                    if (Attributes.Virtual && newAttribute.Type == MethodAttributeType.Static)
                        parseInfo.Script.Diagnostics.Error("Virtual methods cannot be static.", newAttribute.Range);
                }
                
                // Apply the attribute.
                switch (newAttribute.Type)
                {
                    // Apply accessor
                    case MethodAttributeType.Accessor: AccessLevel = newAttribute.AttributeContext.accessor().GetAccessLevel(); break;
                    
                    // Apply static
                    case MethodAttributeType.Static: Static = true; break;
                    
                    // Apply virtual
                    case MethodAttributeType.Virtual: Attributes.Virtual = true; break;
                    
                    // Apply override
                    case MethodAttributeType.Override: Attributes.Override = true; break;
                    
                    // Apply Recursive
                    case MethodAttributeType.Recursive: Attributes.Recursive = true; break;
                }
            }
        }

        // Sets up the method's block.
        public override void SetupBlock()
        {
            if (context.block() != null)
            {
                block = new BlockAction(parseInfo.SetCallInfo(CallInfo), methodScope, context.block());
                ValidateReturns();
            }
            foreach (var listener in listeners) listener.Applied();
        }

        // Makes sure each return statement returns a value if the method returns a value and that each path returns a value.
        private void ValidateReturns()
        {
            ReturnAction[] returns = GetReturns();
            if (doesReturnValue)
            {
                // If there is only one return statement, return the reference to
                // the return statement to reduce the number of actions.
                multiplePaths = returns.Length > 1;

                // Syntax error if there are any paths that don't return a value.
                CheckPath(parseInfo.Script, new PathInfo(block, DocRange.GetRange(context.name), true));

                // Syntax error if a return statement does not return a value.
                foreach (var ret in returns)
                    if (ret.ReturningValue == null)
                        parseInfo.Script.Diagnostics.Error("Must return a value.", ret.ErrorRange);
            }
            else
            {
                // Syntax error on any return statement that returns a value.
                foreach (var ret in returns)
                    if (ret.ReturningValue != null)
                        parseInfo.Script.Diagnostics.Error(Name + " is void, so no value can be returned.", ret.ErrorRange);
            }
        }

        // Gets all return statements in a method.
        private ReturnAction[] GetReturns()
        {
            List<ReturnAction> returns = new List<ReturnAction>();
            getReturns(returns, block);
            return returns.ToArray();

            void getReturns(List<ReturnAction> actions, BlockAction block)
            {
                // Loop through each statement in the block.
                foreach (var statement in block.Statements)
                    // If the current statement is a return statement, add it to the list.
                    if (statement is ReturnAction) actions.Add((ReturnAction)statement);

                    // If the current statement contains sub-blocks, get the return statements in those blocks recursively.
                    else if (statement is IBlockContainer)
                        foreach (var path in ((IBlockContainer)statement).GetPaths())
                            getReturns(actions, path.Block);
            }
        }

        // Makes sure each path returns a value.
        private static void CheckPath(ScriptFile script, PathInfo path)
        {
            bool blockReturns = false;
            // Check the statements backwards.
            for (int i = path.Block.Statements.Length - 1; i >= 0; i--)
            {
                if (path.Block.Statements[i] is ReturnAction)
                {
                    blockReturns = true;
                    break;
                }
                
                if (path.Block.Statements[i] is IBlockContainer)
                {
                    // If any of the paths in the block container has WillRun set to true,
                    // set blockReturns to true. The responsibility of checking if this
                    // block will run is given to the block container.
                    if (((IBlockContainer)path.Block.Statements[i]).GetPaths().Any(containerPath => containerPath.WillRun))
                        blockReturns = true;

                    CheckContainer(script, (IBlockContainer)path.Block.Statements[i]);
                    break;
                }
            }
            if (!blockReturns)
                script.Diagnostics.Error("Path does not return a value.", path.ErrorRange);
        }
        private static void CheckContainer(ScriptFile script, IBlockContainer container)
        {
            foreach (var path in container.GetPaths()) CheckPath(script, path);
        }

        // Checks if the method returns a value.
        override public bool DoesReturnValue() => doesReturnValue;

        // Parses the method.
        override public IWorkshopTree Parse(ActionSet actionSet, MethodCall methodCall)
        {
            actionSet = actionSet.New(actionSet.IndexAssigner.CreateContained());
            
            if (Attributes.WasOverriden && methodCall.ResolveOverrides)
                return ParseVirtual(actionSet, methodCall);
            
            if (Attributes.Recursive && !IsSubroutine)
                return RecursiveStack.Call(this, methodCall, actionSet);

            if (IsSubroutine)
                return ParseSubroutine(actionSet, methodCall);
            
            return ParseNormal(actionSet, methodCall);
        }

        private IWorkshopTree ParseNormal(ActionSet actionSet, MethodCall methodCall)
        {
            // Create the return handler.
            ReturnHandler returnHandler = methodCall.ReturnHandler ?? new ReturnHandler(actionSet, Name, multiplePaths);
            actionSet = actionSet.New(returnHandler);
            
            // Assign the parameters and translate the block.
            AssignParameters(actionSet, ParameterVars, methodCall.ParameterValues);
            block.Translate(actionSet);

            if (methodCall.ResolveReturnHandler) returnHandler.ApplyReturnSkips();
            return returnHandler.GetReturnedValue();
        }

        private IWorkshopTree ParseVirtual(ActionSet actionSet, MethodCall methodCall)
        {
            // Create the switch that chooses the overload.
            SwitchBuilder typeSwitch = new SwitchBuilder(actionSet);

            // Loop through all potential methods.
            IMethod[] options = Attributes.AllOverrideOptions();

            // Get the call settings.
            MethodCall callSettings = new MethodCall(methodCall.ParameterValues, methodCall.AdditionalParameterData) {
                ResolveOverrides = false,
                ResolveReturnHandler = false,
                ReturnHandler = new ReturnHandler(actionSet, Name, true)
            };

            // Parse the current overload.
            typeSwitch.NextCase(new V_Number(((ClassType)Attributes.ContainingType).Identifier));
            Parse(actionSet, callSettings);

            foreach (IMethod option in options)
            {
                // The action set for the overload.
                ActionSet optionSet = actionSet.New(actionSet.IndexAssigner.CreateContained());

                // Add the object variables of the selected method.
                option.Attributes.ContainingType.AddObjectVariablesToAssigner(optionSet.CurrentObject, optionSet.IndexAssigner);

                // Go to next case then parse the block.
                typeSwitch.NextCase(new V_Number(((ClassType)option.Attributes.ContainingType).Identifier));
                option.Parse(optionSet, callSettings);
            }

            ClassData classData = actionSet.Translate.DeltinScript.SetupClasses();

            // Finish the switch.
            typeSwitch.Finish(Element.Part<V_ValueInArray>(classData.ClassIndexes.GetVariable(), actionSet.CurrentObject));

            callSettings.ReturnHandler.ApplyReturnSkips();
            return callSettings.ReturnHandler.GetReturnedValue();
        }

        // Sets up single-instance methods for methods with the 'rule' attribute.
        public void SetupSubroutine()
        {
            if (!IsSubroutine) throw new Exception(Name + " does not have the subroutine attribute.");

            // Setup the subroutine element.
            Subroutine subroutine = parseInfo.TranslateInfo.SubroutineCollection.NewSubroutine(Name);

            // Create the rule.
            TranslateRule subroutineRule = new TranslateRule(parseInfo.TranslateInfo, SubroutineName, subroutine);

            // Setup the return handler.
            ReturnHandler returnHandler = new ReturnHandler(subroutineRule.ActionSet, Name, multiplePaths);
            ActionSet actionSet = subroutineRule.ActionSet.New(returnHandler);

            // Get the variables that will be used to store the parameters.
            IndexReference[] parameterStores = new IndexReference[ParameterVars.Length];
            for (int i = 0; i < ParameterVars.Length; i++)
                parameterStores[i] = actionSet.IndexAssigner.Add(actionSet.VarCollection, ParameterVars[i], true, null, Attributes.Recursive) as IndexReference;
            
            // If the subroutine is an object function inside a class, create a variable to store the class object.
            IndexReference objectStore = null;
            if (Attributes.ContainingType != null && !Static)
            {
                objectStore = actionSet.VarCollection.Assign("_" + Name + "_subroutineStore", true, true);
                Attributes.ContainingType.AddObjectVariablesToAssigner(objectStore.GetVariable(), actionSet.IndexAssigner);
            }
            
            // Set the subroutine info.
            subroutineInfo = new SubroutineInfo(subroutine, returnHandler, subroutineRule, parameterStores, objectStore);

            // Parse the block.
            block.Translate(actionSet);

            // Apply returns.
            returnHandler.ApplyReturnSkips();

            // Add the subroutine.
            parseInfo.TranslateInfo.WorkshopRules.Add(subroutineRule.GetRule());
        }

        // Calls single-instance methods.
        private IWorkshopTree ParseSubroutine(ActionSet actionSet, MethodCall methodCall)
        {
            for (int i = 0; i < subroutineInfo.ParameterStores.Length; i++)
                actionSet.AddAction(subroutineInfo.ParameterStores[i].SetVariable((Element)methodCall.ParameterValues[i]));

            if (subroutineInfo.ObjectStore != null) 
                actionSet.AddAction(subroutineInfo.ObjectStore.SetVariable(actionSet.CurrentObject));

            switch (methodCall.CallParallel)
            {
                // No parallel, call subroutine normally.
                case CallParallel.NoParallel:
                    actionSet.AddAction(Element.Part<A_CallSubroutine>(subroutineInfo.Subroutine));
                    return subroutineInfo.ReturnHandler.GetReturnedValue();
                
                // Restart the subroutine if it is already running.
                case CallParallel.AlreadyRunning_RestartRule:
                    actionSet.AddAction(Element.Part<A_StartRule>(subroutineInfo.Subroutine, EnumData.GetEnumValue(IfAlreadyExecuting.RestartRule)));
                    return null;
                
                // Do nothing if the subroutine is already running.
                case CallParallel.AlreadyRunning_DoNothing:
                    actionSet.AddAction(Element.Part<A_StartRule>(subroutineInfo.Subroutine, EnumData.GetEnumValue(IfAlreadyExecuting.DoNothing)));
                    return null;
                
                default: throw new NotImplementedException();
            }
        }
    
        // Assigns parameters to the index assigner. TODO: Move to OverloadChooser.
        public static void AssignParameters(ActionSet actionSet, Var[] parameterVars, IWorkshopTree[] parameterValues, bool recursive = false)
        {
            for (int i = 0; i < parameterVars.Length; i++)
            {
                actionSet.IndexAssigner.Add(actionSet.VarCollection, parameterVars[i], actionSet.IsGlobal, parameterValues?[i], recursive);

                if (actionSet.IndexAssigner[parameterVars[i]] is IndexReference && parameterValues?[i] != null)
                    actionSet.AddAction(
                        ((IndexReference)actionSet.IndexAssigner[parameterVars[i]]).SetVariable((Element)parameterValues[i])
                    );
            }
        }
    }

    class MethodAttributeHandler
    {
        public MethodAttributeType Type { get; }
        public DocRange Range { get; }
        public DeltinScriptParser.Method_attributesContext AttributeContext { get; }

        public MethodAttributeHandler(DeltinScriptParser.Method_attributesContext attributeContext)
        {
            AttributeContext = attributeContext; 
            Range = DocRange.GetRange(attributeContext);

            if (attributeContext.accessor() != null) Type = MethodAttributeType.Accessor;
            else if (attributeContext.STATIC() != null) Type = MethodAttributeType.Static;
            else if (attributeContext.VIRTUAL() != null) Type = MethodAttributeType.Virtual;
            else if (attributeContext.OVERRIDE() != null) Type = MethodAttributeType.Override;
            else if (attributeContext.RECURSIVE() != null) Type = MethodAttributeType.Recursive;
            else throw new NotImplementedException();
        }

        public void Copy(FileDiagnostics diagnostics)
        {
            diagnostics.Error($"Multiple '{Type.ToString().ToLower()}' attributes.", Range);
        }
    }

    enum MethodAttributeType
    {
        Accessor,
        Static,
        Override,
        Virtual,
        Recursive
    }
}