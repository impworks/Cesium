using Cesium.CodeGen.Contexts;
using Cesium.CodeGen.Ir.Expressions.Constants;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cesium.CodeGen.Ir.Expressions.BinaryOperators;

internal class ComparisonBinaryOperatorExpression: BinaryOperatorExpression
{
    internal ComparisonBinaryOperatorExpression(IExpression left, BinaryOperator @operator, IExpression right)
        : base(left, @operator, right)
    {
        if (!Operator.IsComparison())
            throw new NotSupportedException($"Internal error: operator {Operator} is not comparison.");
    }

    public ComparisonBinaryOperatorExpression(Ast.ComparisonBinaryOperatorExpression expression)
        : base(expression)
    {
    }

    public override IExpression Lower() => Operator switch
    {
        BinaryOperator.GreaterThanOrEqualTo => new ComparisonBinaryOperatorExpression(
            new ComparisonBinaryOperatorExpression(Left.Lower(), BinaryOperator.LessThan, Right.Lower()),
            BinaryOperator.EqualTo,
            new ConstantExpression(new IntegerConstant("0"))
        ),
        BinaryOperator.LessThanOrEqualTo => new ComparisonBinaryOperatorExpression(
            new ComparisonBinaryOperatorExpression(Left.Lower(), BinaryOperator.GreaterThan, Right.Lower()),
            BinaryOperator.EqualTo,
            new ConstantExpression(new IntegerConstant("0"))
        ),
        BinaryOperator.NotEqualTo => new ComparisonBinaryOperatorExpression(
            new ComparisonBinaryOperatorExpression(Left.Lower(), BinaryOperator.EqualTo, Right.Lower()),
            BinaryOperator.EqualTo,
            new ConstantExpression(new IntegerConstant("0"))
        ),
        _ => new ComparisonBinaryOperatorExpression(Left.Lower(), Operator, Right.Lower()),
    };

    public override void EmitTo(IDeclarationScope scope)
    {
        // TODO[#160]: check if operand types are compatible?

        Left.EmitTo(scope);
        Right.EmitTo(scope);
        scope.Method.Body.Instructions.Add(GetInstruction());

        Instruction GetInstruction() => Operator switch
        {
            BinaryOperator.GreaterThan => Instruction.Create(OpCodes.Cgt),
            BinaryOperator.LessThan => Instruction.Create(OpCodes.Clt),
            BinaryOperator.EqualTo => Instruction.Create(OpCodes.Ceq),
            _ => throw new NotSupportedException($"Unsupported binary operator: {Operator}.")
        };
    }

    public override TypeReference GetExpressionType(IDeclarationScope scope) => scope.TypeSystem.Boolean;
}
