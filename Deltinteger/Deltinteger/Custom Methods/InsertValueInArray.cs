using System;
using Deltin.Deltinteger.Parse;
using Deltin.Deltinteger.Elements;

namespace Deltin.Deltinteger.CustomMethods
{
    [CustomMethod("InsertValueInArray", "Inserts a value into an array.", CustomMethodType.Value)]
    class InsertValueInArray : CustomMethodBase
    {
        public override CodeParameter[] Parameters()
        {
            return new CodeParameter[] {
                new CodeParameter("array", "The array to modify."),
                new CodeParameter("index", "Where to insert the value."),
                new CodeParameter("value", "The value to insert.")
            };
        }

        public override IWorkshopTree Get(ActionSet actionSet, IWorkshopTree[] parameters)
        {
            Element array = (Element)parameters[0];
            Element index = (Element)parameters[1];
            Element value = (Element)parameters[2];

            return Element.Part<V_Append>(
                Element.Part<V_Append>(
                    Element.Part<V_ArraySlice>(array, new V_Number(0), index),
                    value
                ),
                Element.Part<V_ArraySlice>(array, index, V_Number.LargeArbitraryNumber)
            );
        }
    }
}