using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// dotnet publish -c Release -r win-x86 --self-contained true

class TerrariaPatcher {
	const int PROCESS_QUERY_INFORMATION = 0x0400;
	const int PROCESS_VM_READ = 0x0010;
	const int PROCESS_VM_WRITE = 0x0020;
	const int PROCESS_VM_OPERATION = 0x0008;
	
	const uint MEM_COMMIT = 0x1000;
	const uint PAGE_NOACCESS = 0x01;
	const uint PAGE_GUARD = 0x100;
	const uint PAGE_EXECUTE_READ = 0x20;
	
	[DllImport("kernel32.dll")]
	static extern IntPtr OpenProcess(int access, bool inherit, int pid);
	
	[DllImport("kernel32.dll")]
	static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr address, byte[] buffer, int size,
										 out int bytesRead);
										 
	[DllImport("kernel32.dll")]
	static extern int VirtualQueryEx(
		IntPtr hProcess,
		IntPtr address,
		out MEMORY_BASIC_INFORMATION buffer,
		uint length);
		
	[DllImport("kernel32.dll")]
	static extern bool CloseHandle(IntPtr handle);
	
	[StructLayout(LayoutKind.Sequential)]
	struct MEMORY_BASIC_INFORMATION {
		public IntPtr BaseAddress;
		public IntPtr AllocationBase;
		public uint AllocationProtect;
		public IntPtr RegionSize;
		public uint State;
		public uint Protect;
		public uint Type;
	}
	
	[DllImport("kernel32.dll")]
	static extern IntPtr VirtualAllocEx(
		IntPtr hProcess,
		IntPtr lpAddress,
		int dwSize,
		uint flAllocationType,
		uint flProtect);
		
	[DllImport("kernel32.dll")]
	static extern bool WriteProcessMemory(
		IntPtr hProcess,
		IntPtr lpBaseAddress,
		byte[] lpBuffer,
		int size,
		out IntPtr written);
		
	const uint MEM_RESERVE = 0x2000;
	const uint PAGE_EXECUTE_READWRITE = 0x40;
	
	[DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
	public static extern void TimeBeginPeriod(int t);
	
	struct Vector2 {
		public float x;
		public float y;
		
		public Vector2(float xx, float yy) {
			x = xx;
			y = yy;
		}
	}
	
	public static IntPtr hProcess = 0;
	public static int playerAddr = 0;
	
	public static bool oldMenuState = false;
	
	static void WriteInt(IntPtr address, int value) {
		byte[] buffer = BitConverter.GetBytes(value);
		
		WriteProcessMemory(
			hProcess,
			address,
			buffer,
			buffer.Length,
			out _);
	}
	
	static void WriteByte(IntPtr address, byte value) {
		//byte[] buffer = BitConverter.GetBytes(value);
		
		byte[] buffer = {value};
		
		WriteProcessMemory(
			hProcess,
			address,
			buffer,
			buffer.Length,
			out _);
	}
	
	static void WriteBytes(IntPtr address, byte[] bytes) {
		WriteProcessMemory(
			hProcess,
			address,
			bytes,
			bytes.Length,
			out _);
	}
	
	static int ReadInt(IntPtr address) {
		byte[] buf = new byte[4];
		
		ReadProcessMemory(
			hProcess,
			address,
			buf,
			4,
			out _);
			
		return BitConverter.ToInt32(buf, 0);
	}
	
	static long ReadLong(IntPtr address) {
		byte[] buf = new byte[8];
		
		ReadProcessMemory(
			hProcess,
			address,
			buf,
			8,
			out _);
			
		return BitConverter.ToInt64(buf, 0);
	}
	
	static bool ReadByte(IntPtr address) {
		byte[] buf = new byte[1];
		
		ReadProcessMemory(
			hProcess,
			address,
			buf,
			1,
			out _);
			
		return BitConverter.ToBoolean(buf, 0);
	}
	
	static Vector2 ReadVector2(IntPtr address) {
		byte[] buf = new byte[8];
		
		ReadProcessMemory(
			hProcess,
			address,
			buf,
			8,
			out _);
			
		float x = BitConverter.ToSingle(buf, 0);
		float y = BitConverter.ToSingle(buf, 4);
		
		return new Vector2(x, y);
	}
	
	static int GetLocalPlayer(IntPtr instruction) {
		byte[] buf = new byte[4];
		
		// player array global
		ReadProcessMemory(hProcess, instruction + 1, buf, 4, out _);
		int playerArrayAddr = BitConverter.ToInt32(buf, 0);
		
		// myPlayer index global
		ReadProcessMemory(hProcess, instruction + 7, buf, 4, out _);
		int myPlayerAddr = BitConverter.ToInt32(buf, 0);
		
		int playerArrayStruct = ReadInt((IntPtr)playerArrayAddr);
		int myPlayer = ReadInt((IntPtr)myPlayerAddr);
		
		int playerPtrAddr = playerArrayStruct + 8 + myPlayer * 4;
		
		return ReadInt((IntPtr)playerPtrAddr);
	}
	
	
	static bool PlayerHasBuff(int buffId) {
		// buff type
		IntPtr buffTypeArray = playerAddr + 0xC8;
		int buffArrayAddr = ReadInt(buffTypeArray);
		
		for (int j = 0; j <= 58; j++) {
			// buff type
			int offset = (0x8 + (j * 0x4));
			IntPtr buffArrSlot = (IntPtr)(buffArrayAddr + offset);
			
			int buffType = ReadInt(buffArrSlot);
			
			if (buffType == buffId)
				return true;
		}
		
		return false;
	}
	
	static void PlayerRemoveBuff(int buffId) {
		// buff type
		IntPtr buffTypeArray = playerAddr + 0xC8;
		int buffArrayAddr = ReadInt(buffTypeArray);
		
		for (int j = 0; j <= 58; j++) {
			// buff type
			int offset = (0x8 + (j * 0x4));
			IntPtr buffArrSlot = (IntPtr)(buffArrayAddr + offset);
			int buffType = ReadInt(buffArrSlot);
			
			// buff time
			IntPtr buffTimeArrayBase = playerAddr + 0xCC;
			int buffTimeAddr = ReadInt(buffTimeArrayBase);
			IntPtr bufftArrSlot = (IntPtr)(buffTimeAddr + offset);
			
			if (buffType == buffId) {
				WriteInt(buffArrSlot, 0);
				WriteInt(bufftArrSlot, 0);
				break;
			}
		}
	}
	
	// ---------- features ---------- //

	static void OverrideRespawnTimer() {
		IntPtr respawnTimeAddr = (IntPtr)(playerAddr + 0x3BC);
		int respawnTimer = ReadInt(respawnTimeAddr);
		
		// sets respawn time to max 3 sec
		if (respawnTimer > 180)
			WriteInt(respawnTimeAddr, 180);
	}
	
	static void DisableFishingLineBreak() {
		IntPtr addr = (IntPtr)(playerAddr + 0x723);
		WriteByte(addr, 1);
	}
	
	static void UnlimitedBuffsCheck() {
		IntPtr inventoryBase = playerAddr + 0xD8;
		int invAddr = ReadInt(inventoryBase);
		
		for (int i = 0; i <= 58; i++) {
			int offset = (0x8 + (i * 0x4));
			
			IntPtr slotPtr = (IntPtr)(invAddr + offset);
			int slotAddr = ReadInt(slotPtr);
			
			// item info
			int itemStack = ReadInt(slotAddr + 0x64);
			
			int itemBuffType = ReadInt(slotAddr + 0xDC);
			bool itemIsConsumable = ReadByte(slotAddr + 0x110);
			
			int HONEY_BUFF_ID = 48;
			
			// check if item is consumable grants a buff when used and disable honey bottles giving honey buff
			if (itemBuffType > 0 && itemBuffType != HONEY_BUFF_ID) {
				if (itemIsConsumable && itemStack >= 30) {
					// buff type
					IntPtr buffArrayBase = playerAddr + 0xC8;
					int buffAddr = ReadInt(buffArrayBase);
					
					// look for empty buff slot
					for (int j = 0; j <= 43; j++) {
						// buff type
						int offs = (0x8 + (j * 0x4));
						IntPtr buffArrSlot = (IntPtr)(buffAddr + offs);
						int buffType = ReadInt(buffArrSlot);
						
						// buff time
						IntPtr buffTimeArrayBase = playerAddr + 0xCC;
						int buffTimeAddr = ReadInt(buffTimeArrayBase);
						IntPtr bufftArrSlot = (IntPtr)(buffTimeAddr + offs);
						
						int wellFedMinor = 26;
						int wellFedMedium = 206;
						int wellFedMajor = 207;
						
						// apply buff
						if (buffType == 0 && !PlayerHasBuff(itemBuffType)) {
							WriteInt(buffArrSlot, itemBuffType);
							WriteInt(bufftArrSlot, 60);				
							break;
						}
					}
				}
			}
		}
	}
	
	static void PatchFossilShatter() {
		string fossilPattern =
			"55 8B EC 57 56 53 83 EC 10 89 4D F0 8B DA 8B 45 0C 66 81 78 04 94 01 0F 85 ?? ?? ?? ?? 83 3D ?? ?? ?? ?? 01 0F 84 ?? ?? ?? ??";
			
		IntPtr addr = FindPattern(fossilPattern);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Fossil signature not found.");
			return;
		}
		
		IntPtr fossilFuncAddr = addr;
		
		Console.WriteLine($"Fossil function at: 0x{fossilFuncAddr.ToString("X")}");
		
		// Patch (xor eax, eax; ret)
		WriteBytes(fossilFuncAddr, new byte[] { 0x31, 0xC0, 0xC3 });
		
		Console.WriteLine("Fossil shatter disabled.");
	}
	
	static void PatchTombstone() {
		string tombstonePattern =
			"55 8B EC 57 56 53 83 EC 30 33 C0 89 45 E0 89 45 E4 89 55 DC 8B F1 83 3D ?? ?? ?? ?? 01 0F 84 ?? ?? ?? ??";
			
		IntPtr addr = FindPattern(tombstonePattern);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Tombstone signature not found.");
			return;
		}
		
		IntPtr tombstoneFuncAddr = addr;
		
		Console.WriteLine($"DropTombstone at: 0x{tombstoneFuncAddr.ToString("X")}");
		
		// Patch: return immediately
		WriteBytes(tombstoneFuncAddr, new byte[] { 0x31, 0xC0, 0xC3 });
		
		Console.WriteLine("Tombstones disabled.");
	}
	
	static void PatchFullRespawnHP() {
		string respawnPattern =
			"80 BE ?? ?? ?? ?? 00 74 ?? 89 96 2C 04 00 00 8B 86 38 04 00 00";
			
		IntPtr addr = FindPattern(respawnPattern);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Respawn HP signature not found.");
			return;
		}
		
		// offset to the JE instruction (74 ??)
		IntPtr respawnPatchAddr = addr + 7;
		
		Console.WriteLine($"Respawn patch at: 0x{respawnPatchAddr.ToString("X")}");
		
		// patch: NOP NOP
		WriteBytes(respawnPatchAddr, new byte[] { 0x90, 0x90 });
		
		Console.WriteLine("Full HP on respawn enabled.");
	}
	
	static void PatchBetterWingsChestChance() {
		string pattern =
			"74 74 8B 03 89 85 ?? ?? ?? ?? 8B 8D ?? ?? ?? ?? BA 28 00 00 00 39 09 E8 ?? ?? ?? ?? 85 C0 75 56 8B 46 04";
			
		IntPtr addr = FindPattern(pattern);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Sky wings chance signature not found.");
			return;
		}
		
		IntPtr penaltyOffset = addr + 17;
		
		Console.WriteLine($"Sky wings chance instruction at: 0x{addr.ToString("X")}");
		
		byte[] newBytes = new byte[] {
			0x04, 0x00, 0x00, 0x00
		};
		
		WriteBytes(penaltyOffset, newBytes);
		
		Console.WriteLine("Sky wings chance 2.5% -> 20% applied.");
	}

	static void PatchTerragrimChance() {
		string pattern =
			"89 85 ?? ?? ?? ?? 8B 8D ?? ?? ?? ?? BA 1E 00 00 00 39 09 E8 ?? ?? ?? ?? 85 C0 75 3C 8B 4D F0 8B 55 EC";
			
		IntPtr addr = FindPattern(pattern);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Terragrim chance instruction not found.");
			return;
		}
		
		IntPtr penaltyOffset = addr + 13;
		
		Console.WriteLine($"Terragrim chance instruction at: 0x{addr.ToString("X")}");
		
		byte[] newBytes = new byte[] {
			0x0A, 0x00, 0x00, 0x00
		};
		
		WriteBytes(penaltyOffset, newBytes);
		
		Console.WriteLine("Terragrim drop chance 3.3% -> 10% applied.");
	}
	
	static void PatchFishingCrateLootOverride() {
		// This exact instruction moves the immediate 703 into the loot variable
		string pattern = "C7 45 D4 BF 02 00 00";
		
		IntPtr addr = FindPattern(pattern);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Wooden fishing crates loot instruction not found.");
			return;
		}
		
		IntPtr fishingCrateLootAddr = addr;
		
		Console.WriteLine($"Wooden fishing crates loot instruction at: 0x{addr.ToString("X")}");
		
		// New immediate: 3093 decimal -> 0x0C15
		byte[] newBytes = new byte[] {
			0xC7, 0x45, 0xD4,
			0x15, 0x0C, 0x00, 0x00
		};
		
		WriteBytes(fishingCrateLootAddr, newBytes);
		
		Console.WriteLine("Added herb bags to wooden fishing crates.");
	}

	static void PatchSwordShrineGeneration() {
		string pattern =
			"DD 5D 9C 80 3D ?? ?? ?? ?? 00 74 0F D1 65 A4 D9 05 ?? ?? ?? ?? DC 7D 9C";
			
		IntPtr addr = FindPattern(pattern);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Sword shrine patch instruction not found.");
			return;
		}
		
		IntPtr pyramidCheckAddr = addr + 10;
		
		Console.WriteLine($"Sword shrine instruction at: 0x{addr.ToString("X")}");
		
		byte[] newBytes = new byte[] {
			0x90, 0x90
		};
		
		WriteBytes(pyramidCheckAddr, newBytes);
		
		Console.WriteLine("Sword shrine patched to always generate.");
	}

	static void PatchPyramidGeneration() {
		string pattern =
			"66 0F D6 47 08 80 3D ?? ?? ?? ?? 00 74 ?? 8B 0D ?? ?? ?? ?? 39 09 E8 ?? ?? ?? ?? 85 C0 75 ?? 8D 4D E4 8D 55 C0";
			
		IntPtr addr = FindPattern(pattern);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Pyramid patch instruction not found.");
			return;
		}
		
		IntPtr pyramidCheckAddr = addr + 12;
		
		Console.WriteLine($"Pyramid instruction at: 0x{addr.ToString("X")}");
		
		byte[] newBytes = new byte[] {
			0x90, 0x90
		};
		
		WriteBytes(pyramidCheckAddr, newBytes);
		
		Console.WriteLine("Pyramid patched to always generate.");
	}
	
	static void PatchFishingLineBreaking() {
		string pattern =
			"8B 08 BA 07 00 00 00 39 09 E8 ?? ?? ?? ?? 85 C0 75 ?? 80 BF ?? ?? ?? ?? 00 75 ?? 8B 46 40 83 78 04 00";
			
		IntPtr addr = FindPattern(pattern);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Fishing line breaking instruction not found.");
			return;
		}
		
		IntPtr pyramidCheckAddr = addr + 3;
		
		Console.WriteLine($"Fishing line breaking instruction at: 0x{addr.ToString("X")}");
		
		// 18 69 F
		byte[] newBytes = new byte[] {
			0x18, 0x69, 0x0F, 0x00
		};
		
		WriteBytes(pyramidCheckAddr, newBytes);
		
		Console.WriteLine("Fishing line will no longer break.");
	}
	
	static void UnnerfReaverShark() {
		string pattern =
			"C7 46 5C ?? ?? ?? ?? C7 46 60 ?? ?? ?? ?? C6 86 ?? ?? ?? ?? ?? C7 46 18 ?? ?? ?? ?? C7 46 1C ?? ?? ?? ?? C7 86 88 ?? ?? ?? ?? ?? ?? ?? C7 46 6C ?? ?? ?? ??";
			
		IntPtr addr = FindPattern(pattern);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Reaver shark patch instruction not found.");
			return;
		}
		
		IntPtr checkAddrUseAnim = addr + 3;
		IntPtr checkAddrUseTime = addr + 10;
		IntPtr checkAddrPickPower = addr + 48;
		
		Console.WriteLine($"Reaver shark instruction at: 0x{addr.ToString("X")}");
		
		// use anim
		byte[] newBytes = new byte[] { 0x12, 0x00, 0x00, 0x00 }; // 18
		WriteBytes(checkAddrUseAnim, newBytes);

		// use time
		newBytes = new byte[] { 0x12, 0x00, 0x00, 0x00 }; // 18
		WriteBytes(checkAddrUseTime, newBytes);

		// pick power
		newBytes = new byte[] { 0x64, 0x00, 0x00, 0x00 }; // 100
		WriteBytes(checkAddrPickPower, newBytes);
		
		Console.WriteLine("Reaver shark unnerfed.");
	}

	static void PatchRespawnTime() {
		string pattern =
			"85 C0 7E ?? B8 10 0E 00 00 EB ?? 83 7D F8 00";
		string pattern2 =
			"85 C0 7E ?? B8 10 0E 00 00 EB ?? 83 7D F4 00";

		string pattern3 =
			"FF 15 ?? ?? ?? ?? 85 C0 0F 85 ?? ?? ?? ?? 83 BE BC 03 00 00 00 7E ?? 8B 86 BC 03 00 00 48 89 45 ?? 81 7D ?? 10 0E 00 00";
		string pattern4 =
			"83 3D ?? ?? ?? ?? 02 0F 85 ?? ?? ?? ?? C6 86 40 07 00 00 01 E9 ?? ?? ?? ?? 8B 86 BC 03 00 00 48 89 45 ?? 81 7D ?? 10 0E 00 00";
	
		string pattern5 = 
			"83 C8 FF EB ?? 81 7D ?? 10 0E 00 00 7E ?? B8 01 00 00 00 EB ?? 33 C0 85 C0 7E ?? B8 10 0E 00 00 EB ?? 83 7D ?? 00 7D ?? 83 C8 FF EB ?? 83 7D ?? 00 7E ?? B8 01 00 00 00 EB ?? 33 C0 85 C0 7D ?? 33 C0 EB ?? 8B 45 ?? 89 86 BC 03 00 00 E9 ?? ?? ?? ?? 8B 46 04 3B 05 ?? ?? ?? ?? 74 ?? 83 3D ?? ?? ?? ?? 02";
		string pattern6 = 
			"83 C8 FF EB ?? 81 7D ?? 10 0E 00 00 7E ?? B8 01 00 00 00 EB ?? 33 C0 85 C0 7E ?? B8 10 0E 00 00 EB ?? 83 7D ?? 00 7D ?? 83 C8 FF EB ?? 83 7D ?? 00 7E ?? B8 01 00 00 00 EB ?? 33 C0 85 C0 7D ?? 33 C0 EB ?? 8B 45 ?? 89 86 BC 03 00 00 83 BE BC 03 00 00 00 7F ?? A1 ?? ?? ?? ?? 3B 46 04 75 ?? A1 ?? ?? ?? ??";

		IntPtr addr = FindPattern(pattern);
		IntPtr addr2 = FindPattern(pattern2);

		IntPtr addr3 = FindPattern(pattern3);
		IntPtr addr4 = FindPattern(pattern4);

		IntPtr addr5 = FindPattern(pattern5);
		IntPtr addr6 = FindPattern(pattern6);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Respawn time patch instruction not found.");
			return;
		}

		if (addr2 == IntPtr.Zero) {
			Console.WriteLine("Respawn time patch instruction 2 not found.");
			return;
		}
		
		if (addr3 == IntPtr.Zero) {
			Console.WriteLine("Respawn time patch instruction 3 not found.");
			return;
		}
		
		if (addr4 == IntPtr.Zero) {
			Console.WriteLine("Respawn time patch instruction 4 not found.");
			return;
		}
		
		IntPtr checkAddrUseAnim = addr + 5;
		IntPtr checkAddrUseAnim2 = addr2 + 5;

		IntPtr checkAddrUseAnim3 = addr3 + 36;
		IntPtr checkAddrUseAnim4 = addr4 + 38;

		IntPtr checkAddrUseAnim5 = addr5 + 8;
		IntPtr checkAddrUseAnim6 = addr6 + 8;

		Console.WriteLine($"Respawn time instruction at: 0x{addr.ToString("X")}");
		
		byte[] newBytes = new byte[] { 0xB4, 0x00, 0x00, 0x00 };
		WriteBytes(checkAddrUseAnim, newBytes);
		WriteBytes(checkAddrUseAnim2, newBytes);
		WriteBytes(checkAddrUseAnim3, newBytes);
		WriteBytes(checkAddrUseAnim4, newBytes);
		WriteBytes(checkAddrUseAnim5, newBytes);
		WriteBytes(checkAddrUseAnim6, newBytes);
		
		Console.WriteLine("Respawn time patched to max 3 seconds.");
	}
	
	static void Main() {
		TimeBeginPeriod(1);
		
		string processName = "Terraria";
		
		Process proc = null;
		Process[] procceses = Process.GetProcessesByName(processName);
		
		while (procceses.Length == 0) {
			procceses = Process.GetProcessesByName(processName);
			
			Console.WriteLine("Waiting for " + processName + ".exe...");
			Thread.Sleep(1000);
		}
		
		proc = procceses[0]; // Process.GetProcessesByName(processName)[0];
		Console.WriteLine("Terraria process found.");

		hProcess = OpenProcess(
					   PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION,
					   false,
					   proc.Id);
					   
		// game menu state
		string gameMenuVarPattern =
			"C7 05 ?? ?? ?? ?? 0A 00 00 00 C6 05 ?? ?? ?? ?? 01 8B 0D ?? ?? ?? ??";
		IntPtr gameMenuAddr = FindPattern(gameMenuVarPattern);
		
		IntPtr saveQuitInstruction = gameMenuAddr + 10;
		
		byte[] buf = new byte[4];
		ReadProcessMemory(hProcess, saveQuitInstruction + 2, buf, 4, out _);
		IntPtr flagAddress = (IntPtr)BitConverter.ToInt32(buf, 0);
		
		// player address
		string pattern = "A1 ?? ?? ?? ?? 8B 15 ?? ?? ?? ?? 3B 50 04 73 ?? 8B 44 90 08 C3";
		
		Console.WriteLine("Scanning memory for player address...");
		IntPtr addr = FindPattern(pattern);
		
		if (addr == IntPtr.Zero) {
			Console.WriteLine("Signature could not found aborting...");
			Thread.Sleep(2000);
			return;
		}
		
		playerAddr = GetLocalPlayer(addr);
		Console.WriteLine($"Local player address: 0x{playerAddr.ToString("X")}");
			
		// features

		PatchTerragrimChance();
		UnnerfReaverShark();
		PatchSwordShrineGeneration();
		PatchPyramidGeneration();

		PatchFishingCrateLootOverride();
		PatchBetterWingsChestChance();
		PatchRespawnTime();
		PatchFishingLineBreaking();
		
		PatchFossilShatter();
		PatchTombstone();
		PatchFullRespawnHP();
		
		/*while (true) {
			// update player address each time menu state is changed
			/*bool inMenu = ReadByte(flagAddress) != false;
			
			if (!oldMenuState && inMenu || oldMenuState && !inMenu) {
				oldMenuState = inMenu;
				
				playerAddr = GetLocalPlayer(addr);
			}
			

			playerAddr = GetLocalPlayer(addr);
			

			OverrideRespawnTimer();
			DisableFishingLineBreak();
		
			UnlimitedBuffsCheck();


			/*if (!inMenu) {
				OverrideRespawnTimer();
				DisableFishingLineBreak();
			
				UnlimitedBuffsCheck();
			}
			
			Thread.Sleep(2);
		}*/
		
		CloseHandle(hProcess);
	}
	
	static IntPtr FindPattern(string pattern) {
		byte?[] pat = ParsePattern(pattern);
		
		long address = 0;
		MEMORY_BASIC_INFORMATION mbi;
		
		while (VirtualQueryEx(hProcess, (IntPtr)address, out mbi,
							  (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))) != 0) {
			bool readable =
				mbi.State == MEM_COMMIT &&
				(mbi.Protect & PAGE_EXECUTE_READ) != 0 ||
				(mbi.Protect & PAGE_EXECUTE_READWRITE) != 0;
				
			if (readable) {
				int size = (int)mbi.RegionSize;
				byte[] buffer = new byte[size];
				
				if (ReadProcessMemory(hProcess, mbi.BaseAddress, buffer, size, out int bytesRead)) {
					int idx = ScanBuffer(buffer, bytesRead, pat);
					
					if (idx >= 0)
						return mbi.BaseAddress + idx;
				}
			}
			
			address += (long)mbi.RegionSize;
		}
		
		return IntPtr.Zero;
	}
	
	static byte?[] ParsePattern(string pattern) {
		string[] parts = pattern.Split(' ');
		byte?[] bytes = new byte?[parts.Length];
		
		for (int i = 0; i < parts.Length; i++) {
			if (parts[i] == "??")
				bytes[i] = null;
			else
				bytes[i] = Convert.ToByte(parts[i], 16);
		}
		
		return bytes;
	}
	
	static int ScanBuffer(byte[] buffer, int length, byte?[] pattern) {
		for (int i = 0; i <= length - pattern.Length; i++) {
			bool match = true;
			
			for (int j = 0; j < pattern.Length; j++) {
				if (pattern[j].HasValue &&
						buffer[i + j] != pattern[j].Value) {
					match = false;
					break;
				}
			}
			
			if (match)
				return i;
		}
		
		return -1;
	}
}