using System;
using System.Collections.Generic;
using System.Linq;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.LanguageServer;

namespace Deltin.Deltinteger.Parse
{
    public interface IStatement
    {
        void Translate(ActionSet actionSet);
    }

    /// <summary>
    /// Anything inherting this interface should also inherit IStatement, but not doing so won't cause any problems.
    /// </summary>
    public interface IBlockContainer
    {
        PathInfo[] GetPaths();
    }

    public class PathInfo
    {
        public BlockAction Block { get; }
        public DocRange ErrorRange { get; }
        public bool WillRun { get; }

        public PathInfo(BlockAction block, DocRange errorRange, bool willRun)
        {
            Block = block;
            ErrorRange = errorRange;
            WillRun = willRun;
        }
    }

    public class ReturnAction : IStatement
    {
        public IExpression ReturningValue { get; }
        public DocRange ErrorRange { get; }

        public ReturnAction(ParseInfo parseInfo, Scope scope, DeltinScriptParser.ReturnContext returnContext)
        {
            ErrorRange = DocRange.GetRange(returnContext.RETURN());
            if (returnContext.expr() != null) ReturningValue = DeltinScript.GetExpression(parseInfo, scope, returnContext.expr());
        }

        public void Translate(ActionSet actionSet)
        {
            if (ReturningValue != null)
                actionSet.ReturnHandler.ReturnValue(ReturningValue.Parse(actionSet));
            actionSet.ReturnHandler.Return();
        }
    }

    public class DeleteAction : IStatement
    {
        private IExpression DeleteValue { get; }

        public DeleteAction(ParseInfo parseInfo, Scope scope, DeltinScriptParser.DeleteContext deleteContext)
        {
            DeleteValue = DeltinScript.GetExpression(parseInfo, scope, deleteContext.expr());

            if (DeleteValue.Type() == null)
                parseInfo.Script.Diagnostics.Error("Expression has no type.", DocRange.GetRange(deleteContext.expr()));
            else if (!DeleteValue.Type().CanBeDeleted)
                parseInfo.Script.Diagnostics.Error($"Type '{DeleteValue.Type().Name}' cannot be deleted.", DocRange.GetRange(deleteContext.expr()));
        }

        public void Translate(ActionSet actionSet)
        {
            // Object reference to delete.
            Element delete = (Element)DeleteValue.Parse(actionSet);

            // Class data.
            var classData = actionSet.Translate.DeltinScript.SetupClasses();

            // Remove the variable from the list of classes.
            actionSet.AddAction(classData.ClassIndexes.SetVariable(
                value: new V_Number(0),
                index: delete
            ));

            // Delete the object.
            DeleteValue.Type().Delete(actionSet, delete);
        }
    }
}