param([Parameter(Mandatory=$true)][string]$Executable,[Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class NativeVersionResource {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr BeginUpdateResource(string name,bool delete);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool UpdateResource(IntPtr handle,IntPtr type,IntPtr name,ushort language,byte[] data,uint size);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool EndUpdateResource(IntPtr handle,bool discard);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string name,IntPtr file,uint flags);
 [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
 delegate bool LanguageCallback(IntPtr module,IntPtr type,IntPtr name,ushort language,IntPtr parameter);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool EnumResourceLanguages(IntPtr module,IntPtr type,IntPtr name,LanguageCallback callback,IntPtr parameter);
 static byte[] Text(string value) {return Encoding.Unicode.GetBytes(value+"\0");}
 static void Align(BinaryWriter writer){while(writer.BaseStream.Position%4!=0)writer.Write((byte)0);}
 static byte[] Block(string key,byte[] value,ushort type,ushort valueLength,params byte[][] children){
  using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
   writer.Write((ushort)0);writer.Write(valueLength);writer.Write(type);writer.Write(Text(key));Align(writer);writer.Write(value);Align(writer);
   foreach(var child in children){writer.Write(child);Align(writer);}var length=stream.Length;
   if(length>ushort.MaxValue)throw new InvalidOperationException("Version resource exceeds its bound.");
   stream.Position=0;writer.Write((ushort)length);return stream.ToArray();
  }
 }
 static byte[] StringValue(string name,string value){return Block(name,Text(value),1,(ushort)(value.Length+1));}
 public static void Apply(string file,string version){
  var v=new Version(version);var fixedInfo=new MemoryStream();using(var writer=new BinaryWriter(fixedInfo,Encoding.UTF8,true)){
   foreach(uint n in new uint[]{0xFEEF04BD,0x10000,((uint)v.Major<<16)|(uint)v.Minor,((uint)v.Build<<16),((uint)v.Major<<16)|(uint)v.Minor,((uint)v.Build<<16),0x3f,0,0x40004,1,0,0,0})writer.Write(n);
  }
  var table=Block("040904B0",new byte[0],1,0,
   StringValue("CompanyName","Ding Ding Projects"),StringValue("FileDescription","Material Docker Image"),
   StringValue("FileVersion",version+".0"),StringValue("InternalName","MaterialDockerImage.exe"),
   StringValue("OriginalFilename","MaterialDockerImage.exe"),StringValue("ProductName","Material Docker Image"),
   StringValue("ProductVersion",version+".0"),StringValue("SquirrelAwareVersion","1"));
  var bytes=Block("VS_VERSION_INFO",fixedInfo.ToArray(),0,52,
   Block("StringFileInfo",new byte[0],1,0,table),
   Block("VarFileInfo",new byte[0],1,0,Block("Translation",new byte[]{9,4,176,4},0,4)));
  var languages=new List<ushort>();var module=LoadLibraryEx(file,IntPtr.Zero,2);
  if(module==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  try{LanguageCallback callback=(m,t,n,l,p)=>{languages.Add(l);return true;};EnumResourceLanguages(module,(IntPtr)16,(IntPtr)1,callback,IntPtr.Zero);GC.KeepAlive(callback);}finally{FreeLibrary(module);}
  var handle=BeginUpdateResource(file,false);if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  bool complete=false;try{
   foreach(var language in languages)if(!UpdateResource(handle,(IntPtr)16,(IntPtr)1,language,null,0))throw new System.ComponentModel.Win32Exception();
   if(!UpdateResource(handle,(IntPtr)16,(IntPtr)1,0x0409,bytes,(uint)bytes.Length))throw new System.ComponentModel.Win32Exception();
   complete=true;
  }finally{if(!EndUpdateResource(handle,!complete))throw new System.ComponentModel.Win32Exception();}
 }
}
'@
$resolved=(Resolve-Path -LiteralPath $Executable).Path
[NativeVersionResource]::Apply($resolved,$Version)
if((Get-Item -LiteralPath $resolved).VersionInfo.FileVersion -ne "$Version.0"){throw 'Native version resource readback failed.'}
