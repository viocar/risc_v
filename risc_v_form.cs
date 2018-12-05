﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace WindowsFormsApp1
{
    public partial class risc_v : Form
    {
        public enum INSN_TYPE
        {
            R, //0
            I, //1
            S, //2
            B, //3
            U, //4
            J, //5
            FENCE, //6
            CSR, //7
            INVALID
        };
        const uint MEMORY_SIZE = 0x4000;
        //I could do REGISTER_NUMBERS dynamically by doing something like "x" + the number but this is more straightforward
        public static readonly string[] REGISTER_NAMES = {  "zero", "ra", "sp", "gp", "tp", "t0", "t1", "t2", "s0", "s1", "a0", "a1", "a2", "a3", "a4", "a5",
                                                            "a6", "a7", "s2", "s3", "s4", "s5", "s6", "s7", "s8", "s9", "s10", "s11", "t3", "t4", "t5", "t6"};
        public static readonly string[] REGISTER_NUMBERS = {"x0", "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8", "x9", "x10", "x11", "x12", "x13", "x14", "x15", "x16",
                                                            "x17", "x18", "x19", "x20", "x21", "x22", "x23", "x24", "x25", "x26", "x27", "x28", "x29", "x30", "x31"};
        public struct insn_parcel
        {
            public uint opcode;
            public INSN_TYPE type;
            public uint rd;
            public uint rs1;
            public uint rs2;
            public uint funct3;
            public uint funct7;
            public short imm;
            public int imm32;
        }
        bool use_reg_names = true; //which register names to use
        bool file_loaded_ok = false; //flipped when file loads
        bool break_after_execution = false; //should we step once or run code?
        byte[] memory = new byte[MEMORY_SIZE]; //RAM
        uint[] registers = new uint[0x20]; //x0 through x31
        uint reg_pc = 0;
        List<string> insn_list = new List<string>(); //holds the instruction listings
        List<String> mem_list = new List<String>();
        List<Label> labels = new List<Label>(); //contains the register labels
        List<TextBox> textboxes = new List<TextBox>(); //contains the register values
        string[] reg_names;
        public risc_v()
        {
            InitializeComponent();
            regb_x0.ReadOnly = true; //x0 must be 0
            button_loadprog.Click += new EventHandler(loadFile);
            button_step.Click += new EventHandler(runCode);
            cbox_register_name.CheckedChanged += new EventHandler(cbox_register_name_CheckedChanged);
            foreach (Label lbl in grp_registers.Controls.OfType<Label>().Where(lbl => lbl.Name.StartsWith("lab_"))) //add labels to a single list
            {
                labels.Add(lbl);
            }
            labels.Reverse(); //for some reason it iterates over them backwards, so we need to reverse the list
            foreach (TextBox tbox in grp_registers.Controls.OfType<TextBox>().Where(tbox => tbox.Name.StartsWith("regb_x"))) //add register textboxes to a single list and register them
            {
                textboxes.Add(tbox);
                tbox.Text = Convert.ToString(0);
                tbox.KeyDown += new KeyEventHandler(regbHandleInputPass);
                tbox.LostFocus += new EventHandler(regbHandleInput);
                tbox.Text = "00000000"; //I have it set to this in the editor but for some reason it resets to 0, so...
            }
            regb_pc.Text = Convert.ToString(0); //PC is a special register, so I don't want it in the textboxes list
            regb_pc.KeyDown += new KeyEventHandler(regbHandleInputPass);
            regb_pc.LostFocus += new EventHandler(regbHandleInput);
            regb_pc.Text = "00000000";
            textboxes.Reverse();
        }
        private void loadFile(object sender, EventArgs e)
        {
            OpenFileDialog loadFileDialog = new OpenFileDialog
            {
                Filter = "rv File|*.rv",
                Title = "Select a .rv File",
                InitialDirectory = "D:\\Program Files\\RISC-V\\assembler\\"
            };
            if (loadFileDialog.ShowDialog() == DialogResult.OK)
            {
                using (BinaryReader rv_prog = new BinaryReader(new FileStream(loadFileDialog.FileName, FileMode.Open)))
                {
                    initializeFields();
                    rv_prog.BaseStream.Seek(0x0, SeekOrigin.Begin);
                    rv_prog.Read(memory, 0, memory.Length);
                    parseInsnText(); //parse the first instruction
                    createMemoryList();
                    file_loaded_ok = true;
                }
            }
        }
        private void runCode(object sender, EventArgs e) //don't think I actually need these args but hey.
        {
            if (file_loaded_ok)
            {
                Button code = sender as Button;
                if (code.Name == "button_step") //for stepping later. right now, the buttons merely step and do not run
                {
                    break_after_execution = true;
                }
                uint insn = BitConverter.ToUInt32(memory, (int)reg_pc);
                executeInstruction(getParcel(insn));
            }
        }
        private void parseInsnText()
        {
            for (int x = 0; x < MEMORY_SIZE / 4; x++) //TODO: Move the for loop to another function so I can modify individual instructions without re-reading all memory
            {
                uint insn = BitConverter.ToUInt32(memory, (x * 4));
                getInsnText(insn, x, getParcel(insn));
            }
            listbox_insns.DataSource = insn_list;
            listbox_insns.DisplayMember = "Item2"; //show the insn
        }
        private insn_parcel getParcel(uint insn) //getParcel and decodeInsn should probably be the same function, but I've split them to make the parts easier to change and update and totally not because I architected my program wrong
        {
            uint opcode = insn & 0x7F;
            switch (opcode) //there are six instruction types. there's no easy way to know which is which beforehand, so use a switch statement to get the right one. should this be an if?
            {
                case 0x33: //ADD SUB SLL SLT SLTU XOR XRL SRA OR AND
                    return decodeInsn(insn, INSN_TYPE.R);
                case 0x3:  //LB LH LW LBU LHU
                case 0x67: //JALR
                    return decodeInsn(insn, INSN_TYPE.I);
                case 0x23: //SB SH SW
                    return decodeInsn(insn, INSN_TYPE.S);
                case 0x63: //BEQ BNE BLT BGE BLTU BGEU
                    return decodeInsn(insn, INSN_TYPE.B);
                case 0x17: //AUIPC
                case 0x37: //LUI
                    return decodeInsn(insn, INSN_TYPE.U);
                case 0x6F: //JAL
                    return decodeInsn(insn, INSN_TYPE.J);
                case 0x13: //ADDI SLTI SLTIU XORI ORI ANDI ... SLLI SRLI SRAI - so this is annoying: these could be either type I or type R. need an extra check for these
                    switch (insn >> 12 & 0x7)
                    {
                        case 0x1:
                        case 0x5:
                            return decodeInsn(insn, INSN_TYPE.R); //shift instructions are sliiightly different from R and should be handled differently. handle later
                        default:
                            return decodeInsn(insn, INSN_TYPE.I);
                    }
                case 0xF: //FENCE FENCE.I
                    return decodeInsn(insn, INSN_TYPE.FENCE);
                case 0x73:
                    return decodeInsn(insn, INSN_TYPE.CSR);
                default:
                    return decodeInsn(insn, INSN_TYPE.INVALID);
            }
        }
        private insn_parcel decodeInsn(uint insn, INSN_TYPE type) //begin pulling the instruction apart to get its data
        {
            uint opcode = insn & 0x7F; //in all so have it outside
            insn_parcel parcel; uint rd = 0; uint funct3 = 0; uint rs1 = 0; uint rs2 = 0; uint funct7 = 0; short imm = 0; int imm32 = 0; //is there a less ugly way to initialize a struct?
            if (type == INSN_TYPE.R)
            {
                rd = insn >> 7 & 0x1F;
                funct3 = insn >> 12 & 0x7;
                rs1 = insn >> 15 & 0x1F;
                rs2 = insn >> 20 & 0x1F;
                funct7 = insn >> 25;
            }
            else if (type == INSN_TYPE.I)
            {
                rd = insn >> 7 & 0x1F;
                funct3 = insn >> 12 & 0x7;
                rs1 = insn >> 15 & 0x1F;
                imm = convertToInt12(Convert.ToInt16(insn >> 20)); //this is ugly to me
            }
            else if (type == INSN_TYPE.S || type == INSN_TYPE.B) //type S and type B can be parsed the same way. B type just does some other weird stuff with bits that can be handled later
            {
                uint imm1 = insn >> 7 & 0x1F;
                funct3 = insn >> 12 & 0x7;
                rs1 = insn >> 15 & 0x1F;
                rs2 = insn >> 20 & 0x1F;
                uint imm2 = insn >> 25;
                if (type == INSN_TYPE.S)
                {
                    imm = convertToInt12((short)(imm1 + (imm2 << 5))); //the way risc-v splits up immediate bits is very annoying
                }
                else if (type == INSN_TYPE.B)
                {
                    uint imm1_part1 = (imm1 & 1) << 11; //I could just one-line all this but I'd prefer to separate it for my sanity's sake
                    uint imm1_part2 = (imm1 & 0x1E);
                    uint imm2_part1 = (imm2 & 0x3F) << 5;
                    uint imm2_part2 = (imm2 & 0x40) << 12;
                    imm = convertToInt12((short)(imm1_part1 + imm1_part2 + imm2_part1 + imm2_part2));
                }
            }
            else if (type == INSN_TYPE.U) //same as S and B. the immediate is totally fucked up for J types lol
            {
                rd = insn >> 7 & 0x1F;
                imm32 = (int)(insn >> 12); //same with this imm
            }
            else if (type == INSN_TYPE.J) //dumb
            {
                rd = insn >> 7 & 0x1F;
                uint imm_c = insn >> 12;
                uint imm_1912 = (imm_c & 0xFF) << 12;
                uint imm_11 = (imm_c & 0x100) << 3;
                uint imm_101 = (imm_c & 0x7FE00) >> 8;
                uint imm_20 = (imm_c & 0x80000);
                imm_c = imm_1912 + imm_11 + imm_101 + imm_20;
                if (imm_c > 0x7FFFF)
                {
                    imm_c = imm_c | 0xFFF80000;
                }
                imm32 = (int)imm_c;
            }
            else if (type == INSN_TYPE.FENCE)
            {
                funct3 = insn >> 12 & 0x7;
            }
            else if (type == INSN_TYPE.CSR)
            {
                rd = insn >> 7 & 0x1F;
                funct3 = insn >> 12 & 0x7;
                rs1 = insn >> 15 & 0x1F;
                imm = Convert.ToInt16(insn >> 20); //it's not signed like other similar immediates
            }
            if (type == INSN_TYPE.INVALID)
            {
                //nothing, but leaving the field in case I need it
            }
            parcel.opcode = opcode; //again, this feels ugly to me
            parcel.type = type;
            parcel.rd = rd;
            parcel.rs1 = rs1;
            parcel.rs2 = rs2;
            parcel.funct3 = funct3;
            parcel.funct7 = funct7;
            parcel.imm = imm;
            parcel.imm32 = imm32;
            return parcel;
        }
        private void executeInstruction(insn_parcel parcel)
        {
            uint opcode = parcel.opcode; int rd = (int)parcel.rd; int rs1 = (int)parcel.rs1; int rs2 = (int)parcel.rs2; uint funct3 = parcel.funct3; //bah gotta cast shit
            uint funct7 = parcel.funct7; short imm = parcel.imm; int imm32 = parcel.imm32; INSN_TYPE type = parcel.type;
            uint op1 = registers[rs1]; //deref rs1
            uint op2 = registers[rs2]; //deref rs2
            bool jump_insn = false; //for J, B, and JALR instructions
            switch (opcode) //there are six instruction types. there's no easy way to know which is which beforehand, so use a switch statement to get the right one. should this be an if?
            {
                case 0x33: //ADD SUB SLL SLT SLTU XOR XRL SRA OR AND
                    if (funct3 == 0)
                    {
                        if (funct7 == 0) //ADD
                        {
                            updateRegister(rd, (op1 + op2));
                        }
                        else if (funct7 == 0x20) //SUB
                        {
                            updateRegister(rd, (op1 - op2));
                        }
                    }
                    else if (funct3 == 0x1) //SLL
                    {
                        updateRegister(rd, (op1 << (int)op2)); //uhh... is this right?
                    }
                    else if (funct3 == 0x2) //SLT
                    {
                        if ((int)op1 < (int)op2)
                        {
                            updateRegister(rd, 1);
                        }
                        else
                        {
                            updateRegister(rd, 0);
                        }
                    }
                    else if (funct3 == 0x3) //SLTU
                    {
                        if (op1 < op2)
                        {
                            updateRegister(rd, 1);
                        }
                        else
                        {
                            updateRegister(rd, 0);
                        }
                    }
                    else if (funct3 == 0x4) //XOR
                    {
                        updateRegister(rd, (op1 ^ op2));
                    }
                    else if (funct3 == 0x5)
                    {
                        if (funct7 == 0) //SRL
                        {
                            updateRegister(rd, (op1 >> (int)op2)); //"If the first operand is an int or long, the right-shift is an arithmetic shift
                        }//                                          (high-order empty bits are set to the sign bit). If the first operand is of type 
                        else if (funct7 == 0x20) //SRA               uint or ulong, the right-shift is a logical shift (high-order bits are zero-filled)."  
                        {
                            updateRegister(rd, (uint)((int)op1 >> (int)op2)); //NOT CORRECTLY WORKING
                        }
                    }
                    else if (funct3 == 0x6) //OR
                    {
                        updateRegister(rd, (op1 | op2));
                    }
                    else //AND
                    {
                        updateRegister(rd, (op1 & op2));
                    }
                    break;
                case 0x3:  //LB LH LW LBU LHU - the spec says that misaligned stores/loads are supported, so I'm not going to enforce alignment
                    uint offset = op1 + (uint)imm;
                    if (MEMORY_SIZE > offset)
                    {
                        if (funct3 == 0) //LB
                        {
                            updateRegister(rd, signExtend(memory[offset], 8)); //LB and LH are sign-extended
                        }
                        else if (funct3 == 0x1) //LH
                        {
                            updateRegister(rd, signExtend(BitConverter.ToUInt16(memory, (int)offset), 16));
                        }
                        else if (funct3 == 0x2) //LW
                        {
                            updateRegister(rd, (BitConverter.ToUInt32(memory, (int)offset)));
                        }
                        else if (funct3 == 0x3) //LBU
                        {
                            updateRegister(rd, memory[offset]); //LBU and LHU are not sign-extended
                        }
                        else if (funct3 == 0x4) //LHU
                        {
                            updateRegister(rd, (uint)(memory[offset] + (memory[offset + 1] << 8)));
                        }
                    }
                    else
                    {
                        illegalInstruction("Instruction at " + Convert.ToString(reg_pc, 16) + " attempted to access memory at 0x" + Convert.ToString(offset, 16) + ", maximum size is 0x" + Convert.ToString(MEMORY_SIZE, 16));
                        //breakexecution
                    }
                    break;
                case 0x67: //JALR
                    jump_insn = true;
                    uint dest = (uint)(reg_pc + op1 + imm);
                    updateRegister(rd, dest);
                    updatePC(dest);
                    break;
                case 0x23: //SB SH SW 
                    offset = op1 + (uint)imm;
                    if (MEMORY_SIZE > offset)
                    {
                        if (funct3 == 0) //SB
                        {
                            memory[offset] = (byte)(op2 & 0xFF);
                            updateMemoryList(offset, 1);
                        }
                        else if (funct3 == 0x1) //SH
                        {
                            memory[offset] = (byte)(op2 & 0xFF);
                            memory[offset + 1] = (byte)((op2 & 0xFF00) >> 8);
                            updateMemoryList(offset, 2);
                        }
                        else if (funct3 == 0x2) //SW
                        {
                            memory[offset] = (byte)(op2 & 0xFF);
                            memory[offset + 1] = (byte)((op2 & 0xFF00) >> 8);
                            memory[offset + 2] = (byte)((op2 & 0xFF0000) >> 16);
                            memory[offset + 3] = (byte)((op2 & 0xFF000000) >> 24);
                            updateMemoryList(offset, 4);
                        }
                    }
                    else
                    {
                        illegalInstruction("Instruction at " + Convert.ToString(reg_pc, 16) + " attempted to access memory at 0x" + Convert.ToString(offset, 16) + ", maximum size is 0x" + Convert.ToString(MEMORY_SIZE, 16));
                    }
                    break;
                case 0x63: //BEQ BNE BLT BGE BLTU BGEU
                    if (funct3 == 0) //BEQ
                    {
                        if (op1 == op2)
                        {
                            jump_insn = true;
                            updatePC(reg_pc + (uint)imm);
                        }
                    }
                    else if (funct3 == 0x1) //BNE
                    {
                        if (op1 != op2)
                        {
                            jump_insn = true;
                            updatePC(reg_pc + (uint)imm);
                        }
                    }
                    else if (funct3 == 0x4) //BLT
                    {
                        if ((int)op1 < (int)op2)
                        {
                            jump_insn = true;
                            updatePC(reg_pc + (uint)imm);
                        }
                    }
                    else if (funct3 == 0x5) //BGE
                    {
                        if ((int)op1 >= (int)op2)
                        {
                            jump_insn = true;
                            updatePC(reg_pc + (uint)imm);
                        }
                    }
                    else if (funct3 == 0x6) //BLTU
                    {
                        if (op1 < op2)
                        {
                            jump_insn = true;
                            updatePC(reg_pc + (uint)imm);
                        }
                    }
                    else if (funct3 == 0x7) //BGEU
                    {
                        if (op1 >= op2)
                        {
                            jump_insn = true;
                            updatePC(reg_pc + (uint)imm);
                        }
                    }
                    break;
                case 0x17: //AUIPC
                    updateRegister(rd, (uint)(imm32 << 12) + reg_pc);
                    break;
                case 0x37: //LUI
                    updateRegister(rd, (uint)imm32 << 12);
                    break;
                case 0x6F: //JAL
                    jump_insn = true;
                    updateRegister(rd, reg_pc + 4);
                    updatePC((uint)imm32);
                    break;
                case 0x13: //ADDI SLTI SLTIU XORI ORI ANDI ... SLLI SRLI SRAI
                    if (funct3 == 0) //ADDI
                    {
                        updateRegister(rd, (uint)(op1 + imm)); //be sure to test with negative numbers
                    }
                    else if (funct3 == 0x1) //SLLI
                    {
                        updateRegister(rd, (op1 << rs2)); //for SLLI, SRLI, and SRAI, rs2 is used as the shift amount and is not a register value
                    }
                    else if (funct3 == 0x2) //SLTI
                    {
                        if ((int)op1 < imm)
                        {
                            updateRegister(rd, 1);
                        }
                        else
                        {
                            updateRegister(rd, 0);
                        }
                    }
                    else if (funct3 == 0x3) //SLTIU
                    {
                        if (op1 < imm)
                        {
                            updateRegister(rd, 1);
                        }
                        else
                        {
                            updateRegister(rd, 0);
                        }
                    }
                    else if (funct3 == 0x4) //XORI
                    {
                        updateRegister(rd, (uint)(op1 ^ imm));
                    }
                    else if (funct3 == 0x5)
                    {
                        if (funct7 == 0) //SRLI
                        {
                            updateRegister(rd, (op1 >> rs2)); //"If the first operand is an int or long, the right-shift is an arithmetic shift
                        }//                                          (high-order empty bits are set to the sign bit). If the first operand is of type 
                        else if (funct7 == 0x20) //SRAI              uint or ulong, the right-shift is a logical shift (high-order bits are zero-filled)."  [same as SRL/SRA]
                        {
                            updateRegister(rd, (uint)((int)op1 >> rs2));
                        }
                    }
                    else if (funct3 == 0x6) //ORI
                    {
                        updateRegister(rd, (op1 | (uint)imm)); //I don't get why this is an error
                    }
                    else //ANDI
                    {
                        updateRegister(rd, (op1 & (uint)imm));
                    }
                    break;
                case 0xF: //FENCE FENCE.I
                case 0x73: //CSR stuff
                case 0:
                    illegalInstruction("Attempted to execute null instruction at " + Convert.ToString(reg_pc, 16));
                    break;
                default:
                    illegalInstruction("Attempted to execute unsupported instruction at " + Convert.ToString(reg_pc, 16));
                    break;
            }
            if (!jump_insn)
            {
                updatePC(reg_pc + 0x4);
            }
        }
        private void getInsnText(uint insn, int offset, insn_parcel parcel)
        {
            string insn_name = "DEFAULT"; //all will have an instruction name
            string operands = "OPERANDS NOT SET";
            uint opcode = parcel.opcode; uint rd = parcel.rd; uint rs1 = parcel.rs1; uint rs2 = parcel.rs2; uint funct3 = parcel.funct3;
            uint funct7 = parcel.funct7; short imm = parcel.imm; int imm32 = parcel.imm32; INSN_TYPE type = parcel.type;
            if (use_reg_names)
            {
                reg_names = REGISTER_NAMES;
            }
            else
            {
                reg_names = REGISTER_NUMBERS;
            }
            if (type == INSN_TYPE.R)
            {
                if (opcode == 0x13) //SLLI SRLI SRAI
                {
                    if (funct3 == 0x1)
                    {
                        insn_name = "slli";
                    }
                    else if (funct3 == 0x5) //not using else for expandability purposes
                    {
                        if (funct7 == 0x20)
                        {
                            insn_name = "srai";
                        }
                        else
                        {
                            insn_name = "srli";
                        }
                    }
                    operands = reg_names[rd] + ", " + reg_names[rs1] + ", 0x" + Convert.ToString(rs2, 16); //probably doesn't need to be in hex, but for consistency's sake, I'm doing it
                }
                else if (opcode == 0x33) //ADD SUB SLL SLT SLTU XOR SRL SRA OR AND
                {
                    if (funct3 == 0)
                    {
                        if (funct7 == 0x20)
                        {
                            insn_name = "sub";
                        }
                        else if (funct7 == 0)
                        {
                            insn_name = "add";
                        }
                    }
                    else if (funct3 == 0x1)
                    {
                        insn_name = "sll";
                    }
                    else if (funct3 == 0x2)
                    {
                        insn_name = "slt";
                    }
                    else if (funct3 == 0x3)
                    {
                        insn_name = "sltu";
                    }
                    else if (funct3 == 0x4)
                    {
                        insn_name = "xor";
                    }
                    else if (funct3 == 0x5)
                    {
                        if (funct7 == 0x20)
                        {
                            insn_name = "sra";
                        }
                        else if (funct7 == 0)
                        {
                            insn_name = "srl";
                        }
                    }
                    else if (funct3 == 0x6)
                    {
                        insn_name = "or";
                    }
                    else //we can use straight else here because the range of possible funct3 operands is exhausted
                    {
                        insn_name = "and";
                    }
                    operands = reg_names[rd] + ", " + reg_names[rs1] + ", " + reg_names[rs2];
                }
            }
            else if (type == INSN_TYPE.I)
            {
                if (opcode == 0x3)
                {
                    if (funct3 == 0)
                    {
                        insn_name = "lb";
                    }
                    else if (funct3 == 0x1)
                    {
                        insn_name = "lh";
                    }
                    else if (funct3 == 0x2)
                    {
                        insn_name = "lw";
                    }
                    else if (funct3 == 0x4)
                    {
                        insn_name = "lbu";
                    }
                    else if (funct3 == 0x5)
                    {
                        insn_name = "lhu";
                    }
                }
                else if (opcode == 0x13) //ADDI SLTI SLTIU XORI ORI ANDI
                {
                    if (funct3 == 0)
                    {
                        insn_name = "addi";
                    }
                    else if (funct3 == 0x2)
                    {
                        insn_name = "slti";
                    }
                    else if (funct3 == 0x3)
                    {
                        insn_name = "sltiu";
                    }
                    else if (funct3 == 0x4)
                    {
                        insn_name = "xori";
                    }
                    else if (funct3 == 0x6)
                    {
                        insn_name = "ori";
                    }
                    else if (funct3 == 0x7)
                    {
                        insn_name = "andi";
                    }
                }
                else if (opcode == 0x67)
                {
                    insn_name = "jalr";
                }
                if (opcode != 0x13) //jalr is I-type but has its own operand format
                {
                    if (imm < 0)
                    {
                        operands = reg_names[rd] + ", -0x" + Convert.ToString(imm * -1, 16).ToUpper() + "(" + reg_names[rs1] + ")";
                    }
                    else
                    {
                        operands = reg_names[rd] + ", 0x" + Convert.ToString(imm, 16).ToUpper() + "(" + reg_names[rs1] + ")";
                    }
                }
                else if (opcode == 0x13)
                {
                    if (imm < 0)
                    {
                        operands = reg_names[rd] + ", " + reg_names[rs1] + ", -0x" + Convert.ToString(imm * -1, 16).ToUpper(); //feels a bit clowny but there ya go
                    }
                    else
                    {
                        operands = reg_names[rd] + ", " + reg_names[rs1] + ", 0x" + Convert.ToString(imm, 16).ToUpper();
                    }
                }
            }
            else if (type == INSN_TYPE.S) //S and B have to be handled differently due to the operand format
            {
                if (opcode == 0x23) //TYPE S
                {
                    if (funct3 == 0)
                    {
                        insn_name = "sb";
                    }
                    else if (funct3 == 1)
                    {
                        insn_name = "sh";
                    }
                    else if (funct3 == 2)
                    {
                        insn_name = "sw";
                    }
                    if (imm < 0)
                    {
                        operands = reg_names[rs2] + ", -0x" + Convert.ToString(imm * -1, 16).ToUpper() + "(" + reg_names[rs1] + ")"; //apparently rs2 is the leftmost operand in this...
                    }
                    else
                    {
                        operands = reg_names[rs2] + ", 0x" + Convert.ToString(imm, 16).ToUpper() + "(" + reg_names[rs1] + ")";
                    }
                }
            }
            else if (type == INSN_TYPE.B)
            {
                if (opcode == 0x63) //all branch opcodes are 0x63, but keeping this here for completeness
                {
                    int target = imm + (offset * 4);
                    string arrow;
                    if (funct3 == 0)
                    {
                        insn_name = "beq";
                    }
                    else if (funct3 == 1)
                    {
                        insn_name = "bne";
                    }
                    else if (funct3 == 4)
                    {
                        insn_name = "blt";
                    }
                    else if (funct3 == 5)
                    {
                        insn_name = "bge";
                    }
                    else if (funct3 == 6)
                    {
                        insn_name = "bltu";
                    }
                    else if (funct3 == 7)
                    {
                        insn_name = "bgeu";
                    }
                    if (target < offset * 4)
                    {
                        arrow = " ▲";
                    }
                    else if (target > offset * 4)
                    {
                        arrow = " ▼";
                    }
                    else
                    {
                        arrow = " ◀";
                    }
                    operands = reg_names[rs1] + ", " + reg_names[rs2] + ", 0x" + Convert.ToString(target, 16).ToUpper() + arrow;
                }
            }
            else if (type == INSN_TYPE.U)
            {
                if (opcode == 0x37) //TYPE U AND J
                {
                    insn_name = "lui";
                }
                else if (opcode == 0x17)
                {
                    insn_name = "auipc";
                }
                operands = reg_names[rd] + ", 0x" + Convert.ToString(imm32, 16).ToUpper();
            }
            else if (type == INSN_TYPE.J)
            {
                int target = imm32 + offset * 4;
                if (opcode == 0x6F)
                {
                    if (parcel.rd == 0)
                    {
                        insn_name = "j";
                    }
                    else
                    {
                        insn_name = "jal";
                    }
                }
                operands = reg_names[rd] + ", 0x" + Convert.ToString(target, 16).ToUpper() + " ▶";
            }
            else if (opcode == 0xF) //TYPE FENCE
            {
                if (funct3 == 0)
                {
                    insn_name = "fence";
                }
                else if (funct3 == 1)
                {
                    insn_name = "fence.i";
                }
                operands = "";
            }
            else if (opcode == 0x73) //all CSR-type instructions are 0x73, but putting it here for completeness
            {
                if (funct3 == 0)
                {
                    if (imm == 0)
                    {
                        insn_name = "ecall";
                    }
                    else if (imm == 1)
                    {
                        insn_name = "ebreak";
                    }
                    operands = "";
                }
                else if (funct3 == 1)
                {
                    insn_name = "csrrw";
                }
                else if (funct3 == 2)
                {
                    insn_name = "csrrs";
                }
                else if (funct3 == 3)
                {
                    insn_name = "csrrc";
                }
                else if (funct3 == 5) //no 4
                {
                    insn_name = "csrrwi";
                }
                else if (funct3 == 6)
                {
                    insn_name = "csrrsi";
                }
                else if (funct3 == 7)
                {
                    insn_name = "csrrci";
                }
                if (funct3 > 0 && funct3 < 4)
                {
                    operands = reg_names[rd] + ", " + reg_names[rs1] + ", 0x" + Convert.ToString(imm, 16).ToUpper();
                }
                else if (funct3 > 4)
                {
                    operands = reg_names[rd] + ", 0x" + Convert.ToString(rs1, 16).ToUpper() + ", 0x" + Convert.ToString(imm, 16).ToUpper();
                }
            }
            else
            {
                insn_name = "data"; //INVALID
                operands = Convert.ToString(insn, 16);
            }
            string pc_ind = " "; //is this the current instruction? shamelessly taking this idea from no$gba
            if (insn_list.Count() * 4 == Convert.ToInt16(regb_pc.Text))
            {
                pc_ind = "●";
            }
            insn_list.Add((Convert.ToString((insn_list.Count() * 4), 16).ToUpper().PadLeft(4, '0') + pc_ind +
                                    Convert.ToString(insn, 16).ToUpper().PadLeft(8, '0') + " " + insn_name.PadRight(8, ' ') + " " + operands));
            //Console.WriteLine(insn_name + " " + operands);
        }
        private void createMemoryList()
        {
            mem_list.Clear();
            listbox_memory.DataSource = null;
            uint rows = MEMORY_SIZE / 0x10;
            for (uint x = 0; x < rows; x++)
            {
                uint offset = x * 0x10;
                string values = "";
                for (uint y = 0; y < 16; y++)
                {
                    values = values + Convert.ToString(memory[offset + y], 16).ToUpper().PadLeft(2, '0') + " ";
                }
                values = Convert.ToString(offset, 16).ToUpper().PadLeft(4, '0') + " " + values;
                mem_list.Add(values);
            }
            listbox_memory.DataSource = mem_list;
        }
        private void updateMemoryList(uint offset, uint size)
        {
            int memory_row = (int)(offset / 16) * 16; //the division acts as rounding in this case
            string values = "";
            listbox_memory.DataSource = null;
            int loops = 1;
            if ((offset % 16) + size > 16) //we may need to update two lines if the values wrap around. 
            {
                loops = 2;
            }
            for (int x = 0; x < loops; x++)
            {
                memory_row = memory_row + (0x10 * x);
                values = "";
                for (int y = 0; y < 16; y++)
                {
                    values = values + Convert.ToString(memory[memory_row + y], 16).ToUpper().PadLeft(2, '0') + " ";
                }
                values = Convert.ToString(memory_row, 16).ToUpper().PadLeft(4, '0') + " " + values;
                mem_list[memory_row / 16] = values;
            }
            listbox_memory.DataSource = mem_list;
        }
        private short convertToInt12(short value)
        {
            if (value > 0xFFF)
            {
                throw new Exception("Value " + value + " is too large to be converted to a 12-bit value.");
            }
            else
            {
                if (value < 0x800) //if it's positive, just return the positive integer
                {
                    return value;
                }
                else //if it's negative
                {
                    return (short)(value | 0xF000);
                }
            }
        }
        private void initializeFields()
        {
            Array.Clear(memory, 0, (int)MEMORY_SIZE);
            insn_list.Clear();
            listbox_insns.DataSource = null;
            listbox_memory.DataSource = null;
        }
        private void regbHandleInputPass(object sender, KeyEventArgs e) //this function just passes the sender along to the main one. it needs to be different due to KeyEventArgs
        {
            if (e.KeyCode == Keys.Enter)
            {
                regbHandleInput(sender, null);
            }
        }
        private void regbHandleInput(object sender, EventArgs e)
        {
            TextBox txt = sender as TextBox;
            int rd = textboxes.IndexOf(txt);
            uint data_value;
            if (!uint.TryParse(txt.Text, System.Globalization.NumberStyles.HexNumber, null, out data_value))
            {
                txt.Text = Convert.ToString(registers[rd], 16).ToUpper().PadLeft(8, '0');
            }
            else if (txt.Name == "regb_x0") //x0 is set to read only, but just in case...
            {
                txt.Text = "00000000";
            }
            else if (txt.Name == "regb_pc") //PC must be divisible by 4 (RV32E not supported)
            {
                if (data_value % 4 == 0 && data_value < MEMORY_SIZE)
                {
                    updatePC(data_value);
                }
                else
                {
                    txt.Text = Convert.ToString(reg_pc, 16).ToUpper().PadLeft(8, '0');
                }
            }
            else
            {
                registers[rd] = data_value;
                txt.Text = Convert.ToString(data_value, 16).ToUpper().PadLeft(8, '0');
            }
        }
        private void updateRegister(int rd, uint val) //just a helper function so that I don't fuck things up
        {
            if (rd != 0)
            {
                registers[rd] = val;
                textboxes[rd].Text = Convert.ToString((val & 0xFFFFFFFF), 16).ToUpper().PadLeft(8, '0');
            }
        }
        private void updatePC(uint new_pc)
        {
            if (new_pc % 4 == 0 && new_pc < MEMORY_SIZE)
            {
                insn_list[(int)reg_pc / 4] = insn_list[(int)reg_pc / 4].Remove(4, 1).Insert(4, " "); //update the insn_list without rebuilding the whole thing
                insn_list[(int)new_pc / 4] = insn_list[(int)new_pc / 4].Remove(4, 1).Insert(4, "●");
                listbox_insns.DataSource = null;
                listbox_insns.DisplayMember = "";
                listbox_insns.DataSource = insn_list;
                reg_pc = new_pc;
                regb_pc.Text = Convert.ToString(new_pc, 16).ToUpper().PadLeft(8, '0'); //"should" this be reg_pc instead? I know it makes no difference execution-wise but, you know
            }
            else
            {
                Console.WriteLine("Attempted to jump to misaligned or out-of-range address.");
                //breakexecution
            }
        }
        private void illegalInstruction(string message)
        {
            break_after_execution = true;
            Console.WriteLine(message);
        }
        private uint signExtend(uint value, int size) //truncate a value to the selected size then sign extend it
        {
            Console.WriteLine(size);
            if (size == 8)
            {
                if ((value & 0x80) != 0)
                {
                    return value | 0xFFFFFF00;
                }
            }
            else if (size == 16)
            {
                if ((value & 0x8000) != 0)
                {
                    return value | 0xFFFF0000;
                }
            }
            else
            {
                Console.WriteLine("Sign extend must use 8 or 16 bit values");
            }
            return value;
        }
        private void cbox_register_name_CheckedChanged(object sender, EventArgs e) //this just feels ugly to me
        {
            use_reg_names = !use_reg_names;
            if (use_reg_names)
            {
                reg_names = REGISTER_NAMES;
            }
            else
            {
                reg_names = REGISTER_NUMBERS;
            }
            for (byte i = 0; i < 32; i++)
            {
                labels[i].Text = reg_names[i];
            }
            insn_list.Clear();
            listbox_insns.DataSource = null;
            listbox_insns.DisplayMember = ""; //trying to clear the list...?
            if (file_loaded_ok)
            {
                parseInsnText(); //parse the first instruction
            }
        }
    }
}
