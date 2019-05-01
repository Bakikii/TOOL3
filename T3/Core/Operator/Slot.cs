using System;
using System.Diagnostics;
using System.Reflection;
using SharpDX;

namespace T3.Core.Operator
{

    public class EvaluationContext
    {
    }

    public class OutputAttribute : Attribute
    {
    }

    public class InputAttribute : Attribute
    {
    }

    public class FloatInputAttribute : InputAttribute
    {
        public float DefaultValue { get; set; }
    }

    public class StringInputAttribute : InputAttribute
    {
        public string DefaultValue { get; set; }
    }

    public class IntInputAttribute : InputAttribute
    {
        public int DefaultValue { get; set; }
    }

    public interface IConnectableSource
    {
    }

    public interface IConnectableTarget
    {
        void AddConnectionSource(IConnectableSource sourceSlot);
        void RemoveConnectionSource();
        bool IsConnected { get; }
    }

    public abstract class Slot : IConnectableSource, IConnectableTarget
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = String.Empty;
        public Type Type { get; protected set; }

        public abstract void AddConnectionSource(IConnectableSource sourceSlot);
        public abstract void RemoveConnectionSource();
        public abstract bool IsConnected { get; }
    }

    public abstract class InputValue
    {
        public Type ValueType;
        public abstract InputValue Clone();
        public abstract void Assign(InputValue otherValue);
    }

    public class InputValue<T> : InputValue
    {
        public InputValue()
            : this(default(T))
        {
        }

        public InputValue(T value)
        {
            Value = value;
            ValueType = typeof(T);
        }

        public override InputValue Clone()
        {
            return new InputValue<T>(Value);
        }

        public override void Assign(InputValue otherValue)
        {
            if (otherValue is InputValue<T> otherTypedValue)
            {
                Value = otherTypedValue.Value;
            }
            else
            {
                Debug.Assert(false); // trying to assign different types of input values
            }
        }

        public T Value;
    }

    public class Slot<T> : Slot
    {
        public T Value;// { get; set; }
        public bool IsDirty { get; set; } = true;

        public Slot()
        {
            UpdateAction = Update;
            Type = typeof(T);
        }

        public Slot(Action<EvaluationContext> updateAction) : this()
        {
            UpdateAction = updateAction;
        }

        public Slot(Action<EvaluationContext> updateAction, T defaultValue) : this()
        {
            UpdateAction = updateAction;
            Value = defaultValue;
        }

        public Slot(T defaultValue) : this()
        {
            Value = defaultValue;
        }

        public void Update(EvaluationContext context)
        {
            if (InputConnection != null)
            {
                Value = InputConnection.GetValue(context);
            }
        }

        public T GetValue(EvaluationContext context)
        {
            UpdateAction?.Invoke(context); // no caching atm
            return Value;
        }

        public override void AddConnectionSource(IConnectableSource sourceSlot)
        {
            InputConnection = (Slot<T>)sourceSlot;
        }

        public override void RemoveConnectionSource()
        {
            InputConnection = null;
        }

        public override bool IsConnected => InputConnection != null;

        private Slot<T> _inputConnection;
        public Slot<T> InputConnection
        {
            get => _inputConnection;
            set
            {
                _inputConnection = value;
                IsDirty = true;
            }
        }

        public Action<EvaluationContext> UpdateAction;
    }

    public interface IOutputSlot
    {
        Guid Id { get; }

    }


    public interface IInputSlot : IConnectableSource, IConnectableTarget
    {
        Guid Id { get; set; }
        SymbolChild.Input Input { get; set; }
//         void AddConnection(Slot slot);
//         void RemoveConnection();
//         bool IsConnected { get; }
    }

    public class InputSlot<T> : Slot<T>, IInputSlot
    {
        public InputSlot(InputValue<T> typedInputValue)
        {
            UpdateAction = Update;
            TypedInputValue = typedInputValue;
        }

        public InputSlot()
            : this(default(T))
        {
        }

        public InputSlot(T value)
            : this(new InputValue<T>(value))
        {
        }

        public void Update(EvaluationContext context)
        {
            Value = InputConnection != null ? InputConnection.GetValue(context)
                                            : Input.IsDefault ? TypedDefaultValue.Value
                                                              : TypedInputValue.Value;
        }

        private SymbolChild.Input _input;
        public SymbolChild.Input Input
        {
            get => _input;
            set
            {
                _input = value;
                TypedInputValue = (InputValue<T>)value.Value;
                TypedDefaultValue = (InputValue<T>)value.DefaultValue;
            }
        }

        public InputValue<T> TypedInputValue;
        public InputValue<T> TypedDefaultValue;
    }

    public class Size2Slot : InputSlot<Size2>
    {
        public Size2Slot(Size2 defaultValue)
        : base(defaultValue)
        {
            UpdateAction = Update;
        }

        public Size2Slot(InputValue<Size2> typedInputValue)
            : base(typedInputValue)
        {
            UpdateAction = Update;
        }

        public new void Update(EvaluationContext context)
        {
            if (InputConnection != null)
                Value = InputConnection.GetValue(context);
            else
            {
                if (Width.InputConnection != null)
                    Value.Width = Width.GetValue(context);
                if (Height.InputConnection != null)
                    Value.Height = Height.GetValue(context);
            }
        }

        public InputSlot<int> Width  = new InputSlot<int>(new InputValue<int>(0));
        public InputSlot<int> Height = new InputSlot<int>(new InputValue<int>(0));
    }


    public class ConverterSlot<TFrom, TTo> : Slot<TTo> 
    {
        readonly Func<TFrom, TTo> _converterFunc;

        public ConverterSlot(Slot<TFrom> sourceSlot, Func<TFrom, TTo> converterFunc)                                                                            
        {
            UpdateAction = Update;
            SourceSlot = sourceSlot;
            //var floatToInt = new Converter2<float, int>(f => (int)f);
            _converterFunc = converterFunc;
        }

        private Slot<TFrom> SourceSlot { get; }

        public void Update(EvaluationContext context)
        {
            Value = _converterFunc(SourceSlot.GetValue(context));
        }
    }


}