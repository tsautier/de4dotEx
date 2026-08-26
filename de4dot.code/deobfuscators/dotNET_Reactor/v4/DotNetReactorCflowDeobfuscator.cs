using System.Collections.Generic;
using System.Linq;
using de4dot.blocks;
using de4dot.blocks.cflow;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4;

/*
	IL_1AD1: ldc.i4    21
	IL_1AD6: call      bool BoolFunc() // returns true
	IL_1ADB: brfalse   IL_0012
	IL_1AE0: pop
	IL_1AE1: ldc.i4    21
	IL_1AE6: br        IL_0012
*/
class DotNetReactorCflowDeobfuscator : IBlocksDeobfuscator {
	bool _hasSwitch;

	public bool ExecuteIfNotModified { get; }

	public void DeobfuscateBegin(Blocks blocks) => _hasSwitch = blocks.Method.Body.Instructions.Any(instr => instr.OpCode == OpCodes.Switch);

	public bool Deobfuscate(List<Block> allBlocks) {
		if (!_hasSwitch)
			return false;

		var modified = false;
		foreach (var block in allBlocks) {
			var instrs = block.Instructions;
			if (instrs.Count < 2)
				continue;

			var lastInstr = block.LastInstr;
			if (!lastInstr.IsBrtrue() && !lastInstr.IsBrfalse())
				continue;

			var callIndex = instrs.IndexOf(block.LastInstr) - 1;
			var call = instrs[callIndex];
			if (call.OpCode.Code != Code.Call)
				continue;

			if (block.FallThrough == null)
				continue;
			var pop = block.FallThrough.FirstInstr;
			if (pop.OpCode.Code != Code.Pop)
				continue;

			if (call.Operand is not MethodDef { HasBody: true } method)
				continue;
			var methodInstrs = method.Body.Instructions;
			if (methodInstrs.Count < 2)
				continue;

			var flag = method.ReturnType.FullName == typeof(bool).FullName && methodInstrs[methodInstrs.Count - 2].OpCode.Code != Code.Ldc_I4_0;
			block.Replace(callIndex, 1, flag ? OpCodes.Ldc_I4_1.ToInstruction() : OpCodes.Ldc_I4_0.ToInstruction());

			modified = true;

			if (callIndex > 0 && instrs[callIndex - 1].OpCode == OpCodes.Call) {
				// Special case in some samples that ordinarily use CflowDeobfuscator2, but have a chain
				// call method2  (dead)
				// call method   (already replaced by block.Replace() at this point)
				// brtrue (--> pop)
				// pop
				if (call.Operand is not MethodDef method2 || method2.DeclaringType != method.DeclaringType || method2.Parameters.Count != 0)
					continue;
				var methodInstrs2 = method2.Body.Instructions;
				if (methodInstrs2.Count < 2)
					continue;

				var flag2 = method2.ReturnType.FullName == typeof(bool).FullName && methodInstrs2[methodInstrs2.Count - 2].OpCode.Code != Code.Ldc_I4_0;
				block.Replace(callIndex - 1, 1, flag2 ? OpCodes.Ldc_I4_1.ToInstruction() : OpCodes.Ldc_I4_0.ToInstruction());
			}
		}

		return modified;
	}
}

/*
  Actual method IL:
	IL_140B: ldc.i4    326
	IL_1410: stloc     V_53
	IL_1414: br        IL_3928
	IL_1419: brtrue    IL_270A

	IL_3928: call      bool BoolFunc()  // returns true
	IL_392D: br        IL_1419

  Cflow analysis auto-merges it to this, which will be the BlocksDeobfuscator input:
	IL_140B: ldc.i4    326
	IL_1410: stloc     V_53
	IL_3928: call      bool BoolFunc()  // returns true
	IL_1419: brtrue    IL_270A
*/
class DotNetReactorCflowDeobfuscator2 : IBlocksDeobfuscator {
	bool _hasSwitch;

	public bool ExecuteIfNotModified { get; }

	public void DeobfuscateBegin(Blocks blocks) => _hasSwitch = blocks.Method.Body.Instructions.Any(instr => instr.OpCode == OpCodes.Switch);

	public bool Deobfuscate(List<Block> allBlocks) {
		if (!_hasSwitch)
			return false;

		var modified = false;
		foreach (var block in allBlocks) {
			var instrs = block.Instructions;
			if (instrs.Count < 4 || !block.IsConditionalBranch())
				continue;

			var callIndex = instrs.IndexOf(block.LastInstr) - 1;
			var call = instrs[callIndex];
			if (call.OpCode != OpCodes.Call
				    || !instrs[callIndex - 1].IsStloc()
				    || !instrs[callIndex - 2].IsLdcI4())
				continue;

			if (call.Operand is not MethodDef { HasBody: true } method)
				continue;

			var methodInstrs = method.Body.Instructions;
			if (methodInstrs.Count < 2)
				continue;

			var flag = method.ReturnType.FullName == typeof(bool).FullName && methodInstrs[methodInstrs.Count - 2].OpCode.Code != Code.Ldc_I4_0;
			block.Replace(callIndex, 1, flag ? OpCodes.Ldc_I4_1.ToInstruction() : OpCodes.Ldc_I4_0.ToInstruction());

			modified = true;
		}

		return modified;
	}
}
