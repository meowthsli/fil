using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Irony.Ast;
using Irony.Parsing;
using Irony.Interpreter.Ast;

namespace FILLang
{
    /// <summary>
    /// Grammar for Fil language pieces
    /// </summary>
    [Language("FIL", "0.1a", "FIL spec by Temerev")]
    public class FilGrammar : Grammar
    {
        public FilGrammar()
        {
            var number = CreateNumberTerminal("number");

            var percent = CreatePercentTerminal("percent");

            var identifierPart = CreateIdentifierTerminal("identFragment");
            var identifier = new NonTerminal("ident");
            identifier.Rule = MakePlusRule(identifier, identifierPart);
            identifier.AstConfig = new AstNodeConfig
                {
                    NodeCreator = (ctx, node) =>
                        {
                            var @in =  new MultipartIdentifierNode();
                            @in.Init(ctx, node);
                            node.AstNode = @in;
                        }
                };

            var key = CreateKeywordTerminal("keyword");

            var lbr = ToTerm("[");
            var rbr = ToTerm("]");
            lbr.SetFlag(TermFlags.NoAstNode | TermFlags.IsTransient);
            rbr.SetFlag(TermFlags.NoAstNode | TermFlags.IsTransient);

            var instr = new NonTerminal("instr");
            var frag = new NonTerminal("fragment");
            frag.Rule = number | percent | identifier | key | lbr + instr + rbr;
            frag.AstConfig = new AstNodeConfig {NodeType = typeof (AstNode)};

            instr.Rule = MakePlusRule(instr, frag);
            instr.AstConfig = new AstNodeConfig {NodeType = typeof (InstrCallNode)};

            var colon = ToTerm(":");
            colon.SetFlag(TermFlags.IsTransient | TermFlags.NoAstNode);
            var assign = new NonTerminal("assign");
            assign.Rule = identifier + colon + instr;
            assign.AstConfig = new AstNodeConfig {NodeType = typeof (AssignNode)};

            var lineBody = new NonTerminal("lineBody");
            lineBody.Rule = instr | assign;
            lineBody.AstConfig = new AstNodeConfig
                {
                    NodeCreator = (ctx, node) =>
                        {
                            // lift
                            node.AstNode = node.ChildNodes[0].AstNode;
                        }
                };

            var dot = ToTerm(".");
            dot.SetFlag(TermFlags.NoAstNode | TermFlags.IsTransient);
            var line = new NonTerminal("line");
            line.AstConfig = new AstNodeConfig
                {
                    NodeCreator = (ctx, node) =>
                        {
                            // lift
                            node.AstNode = node.ChildNodes[0].AstNode;
                        }
                };
            line.Rule = lineBody + dot;

            var prog = new NonTerminal("prog");
            prog.Rule = MakePlusRule(prog, line);
            prog.AstConfig = new AstNodeConfig {NodeType = typeof(StatementListNode)};

            Root = prog;
            LanguageFlags |= LanguageFlags.CreateAst;
        }

        private static IdentifierTerminal CreateIdentifierTerminal(string identifier)
        {
            var identifierTerminal = new IdentifierTerminal(identifier, "-_");
            identifierTerminal.CaseRestriction = CaseRestriction.AllLower;
            identifierTerminal.AstConfig = new AstNodeConfig {NodeType = typeof (AstNode)};
            return identifierTerminal;
        }

        private static IdentifierTerminal CreateKeywordTerminal(string identifier)
        {
            var keyword = new IdentifierTerminal(identifier);
            keyword.CaseRestriction = CaseRestriction.AllUpper;
            keyword.AstConfig = new AstNodeConfig {NodeType = typeof (NamedKeyword)};
            return keyword;
        }

        private static NumberLiteral CreateNumberTerminal(string name)
        {
            var numberLiteral = new NumberLiteral(name)
                {
                    AstConfig = new AstNodeConfig {NodeType = typeof (LiteralValueNode)},
                    DefaultIntTypes = new[]
                        {
                            TypeCode.Int32,
                            TypeCode.Int64,
                        }
                };
            numberLiteral.Options |= NumberOptions.AllowSign
                                     | NumberOptions.AllowUnderscore
                                     | NumberOptions.DisableQuickParse;
            return numberLiteral;
        }

        private static NumberLiteral CreatePercentTerminal(string name)
        {
            var literal = new NumberLiteral(name)
                {
                    AstConfig = new AstNodeConfig
                        {
                            NodeCreator = (ctx, tree) =>
                                {
                                    var n = new PercentLiteralNode();
                                    n.Init(ctx, tree);
                                }
                        },
                    DefaultIntTypes = new[]
                        {
                            TypeCode.Decimal,
                        }
                };
            literal.AddSuffix("%", TypeCode.Decimal);
            literal.Options |= NumberOptions.AllowSign
                                     | NumberOptions.AllowUnderscore;
            return literal;

            //TerminalFactory.CreateVbNumber()
        }
    }

    public class NamedKeyword : AstNode
    {
        public override void Init(AstContext context, ParseTreeNode treeNode)
        {
            this.Term = treeNode.Term;
            this.Span = treeNode.Span;
            this.ErrorAnchor = this.Location;
            treeNode.AstNode = (object) this;

            Key = treeNode.Token.ValueString;
        }

        public string Key;
    }

    public class InstrCallNode : AstNode
    {
        public override void Init(AstContext context, ParseTreeNode treeNode)
        {
            this.Term = treeNode.Term;
            this.Span = treeNode.Span;
            this.ErrorAnchor = this.Location;
            treeNode.AstNode = (object)this;

            var nodes = treeNode.ChildNodes.Select(
                x =>
                    {
                        var cn = x.ChildNodes;
                        if (cn.Count > 0)
                            return new
                                {
                                    AstNode = (AstNode) cn[0].AstNode,
                                };
                        return null;
                    }
                ).Where(x => x != null).ToArray();

            Keywords = string.Join("_",
                nodes.Select(x => x.AstNode)
                    .OfType<NamedKeyword>()
                    .Select(nk => nk.Key)
                    .ToArray());

            Paramz = nodes.Select(x => x.AstNode)
                           .Except(nodes.Select(x => x.AstNode).OfType<NamedKeyword>())
                           .ToArray();
            
            ChildNodes.AddRange(Paramz);
            AsString = Keywords + "(..)";
        }

        public string Keywords;
        public AstNode[] Paramz;
    }

    public class AssignNode : AstNode
    {
        public override void Init(AstContext context, ParseTreeNode treeNode)
        {
            this.Term = treeNode.Term;
            this.Span = treeNode.Span;
            this.ErrorAnchor = this.Location;
            treeNode.AstNode = (object)this;

            Target = (MultipartIdentifierNode)treeNode.ChildNodes[0].AstNode;
            Source = (AstNode) treeNode.ChildNodes[1].AstNode;
            AsString = Target.AsString + " = ...";

            ChildNodes.Add(Source);
            ChildNodes.Add(Target);
        }

        public MultipartIdentifierNode Target;
        public AstNode Source;
    }

    public class MultipartIdentifierNode : IdentifierNode
    {
        public override void Init(AstContext context, ParseTreeNode treeNode)
        {
            this.Term = treeNode.Term;
            this.Span = treeNode.Span;
            this.ErrorAnchor = this.Location;
            treeNode.AstNode = (object)this;

            Symbol = string.Join("_", treeNode.ChildNodes.Select(x => x.Token.ValueString));
            AsString = this.Symbol;
        }
    }

    public class PercentLiteralNode : LiteralValueNode
    {
        public override string AsString
        {
            get { return base.AsString; }
            protected set
            {
                base.AsString = value + "%";
                base.Value = AsString;
            }
        }
    }

    public class PythonGenerator
    {
        public string Run(string text)
        {
            var p = new Parser(new FilGrammar());
            try
            {
                var parseTree = p.Parse(text);
                if (parseTree.ParserMessages.Count > 0)
                {
                    return parseTree.ParserMessages[0].ToString();
                }

                var ast = (StatementListNode)parseTree.Root.AstNode;
                var indent = 0;
                Generate(ast, ref indent);
                return _text.ToString();
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }

        public StringBuilder _text = new StringBuilder();
 
        private void Generate(StatementListNode ast, ref int indent)
        {
            foreach (var st in ast.ChildNodes)
            {
                var t = GenerateStdlib(st, ref indent);
                foreach (var sdlib in t)
                    _text.Append(sdlib + Environment.NewLine + Environment.NewLine);
            }

            foreach (var st in ast.ChildNodes)
                _text.Append(Dispatch(st, ref indent) + Environment.NewLine);
        }

        public string Dispatch(AstNode st, ref int indent)
        {
            if (st is InstrCallNode)
                return GenerateInstrCall((InstrCallNode)st, ref indent);
            else if (st is AssignNode)
                return GenerateAssign((AssignNode)st, ref indent);
            else if (st is LiteralValueNode)
                return GenerateLiteral((LiteralValueNode)st, ref indent);
            else if (st is MultipartIdentifierNode)
                return GenerateIdent((MultipartIdentifierNode)st, ref indent);
            else
                throw new NotSupportedException();
        }

        public string[] GenerateStdlib(AstNode st, ref int indent)
        {
            if (st is InstrCallNode)
                return GenerateInstrDef((InstrCallNode)st, ref indent);
            else if (st is AssignNode)
                return GenerateStdlib(((AssignNode) st).Source, ref indent);
            return new string[0];
        }

        private string GenerateInstrCall(InstrCallNode st, ref int indent)
        {
            return st.Keywords + "("
                   + string.Join(", ", st.Paramz.Select(
                       x =>
                           {
                               var indent0 = 0;
                               return Dispatch(x, ref indent0);
                           }).ToArray()) + ")";
        }

        private string[] GenerateInstrDef(InstrCallNode st, ref int indent)
        {
            var list = new List<string>();
            foreach(var x in st.Paramz)
                list.AddRange(GenerateStdlib(x, ref indent));

            var def = "def " + st.Keywords + "("
                   + string.Join(", ", Enumerable.Range(0, st.Paramz.Count())
                    .Select(x => "p" + x))
                    + "): " + Environment.NewLine + "\tpass";
            list.Add(def);

            return list.ToArray();
        }

        private string GenerateAssign(AssignNode st, ref int indent)
        {
            return GenerateIdent(st.Target, ref indent) + " = " + Dispatch(st.Source, ref indent);
        }

        private string GenerateLiteral(LiteralValueNode st, ref int indent)
        {
            return Convert.ToString(st.Value);
        }

        private string GenerateIdent(MultipartIdentifierNode st, ref int indent)
        {
            return st.AsString;
        }
    }
}
