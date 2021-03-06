using System;
using System.Linq;
using System.Collections.Generic;
using Deltin.Deltinteger.Parse;

namespace Deltin.Deltinteger
{
    public class MethodAttributes
    {
        /// <summary>The type the method belongs to.</summary>
        public CodeType ContainingType { get; set; }

        ///<summary>If true, the method can be called asynchronously.</summary>
        public bool Parallelable { get; set; } = false;

        ///<summary>If true, the method can be overriden.</summary>
        public bool Virtual { get; set; } = false;

        ///<summary>If true, the method must be overriden.</summary>
        public bool Abstract { get; set; } = false;

        ///<summary>If true, the method is overriding another method.</summary>
        public bool Override { get; set; } = false;

        ///<summary>Determines if the method can be overriden. This will return true if the method is virtual, abstract, or overriding another method.</summary>
        public bool IsOverrideable => Virtual || Abstract || Override;

        /// <summary>Determines if the method was overriden.</summary>
        public bool WasOverriden => AllOverrideOptions().Length > 0;

        /// <summary>An array of methods that directly overrides the function. Call `AllOverrideOptions` instead for all child overriders.</summary>
        public IMethod[] Overriders => _overriders.ToArray();
        
        /// <summary>Determines if the method can be called recursively.</summary>
        public bool Recursive { get; set; }

        private readonly List<IMethod> _overriders = new List<IMethod>();

        public MethodAttributes() {}

        public MethodAttributes(bool isParallelable, bool isVirtual, bool isAbstract)
        {
            Parallelable = isParallelable;
            Virtual = isVirtual;
            Abstract = isAbstract;
        }

        public void AddOverride(IMethod overridingMethod)
        {
            _overriders.Add(overridingMethod);
        }

        public IMethod[] AllOverrideOptions()
        {
            List<IMethod> options = new List<IMethod>();

            options.AddRange(_overriders);

            foreach (var overrider in _overriders)
                options.AddRange(overrider.Attributes.AllOverrideOptions());
            
            return options.ToArray();
        }
    }

    public class MethodCall
    {
        public IWorkshopTree[] ParameterValues { get; }
        public object[] AdditionalParameterData { get; }
        public CallParallel CallParallel { get; set; } = CallParallel.NoParallel;
        public ReturnHandler ReturnHandler { get; set; } = null;
        public bool ResolveOverrides { get; set; } = true;
        public bool ResolveReturnHandler { get; set; } = true;

        public MethodCall(IWorkshopTree[] parameterValues, object[] additionalParameterData)
        {
            ParameterValues = parameterValues;
            AdditionalParameterData = additionalParameterData;
        }
    }
}