﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace lngpack
{
	/// <summary>
	/// Metro 2033 .lng ファイル Packer.
	/// </summary>
	public sealed class Program
	{
		/// <summary>
		/// スペース
		/// </summary>
		private const char CHAR_SPACE = ' ';
		/// <summary>
		/// サーカムフレックス
		/// </summary>
		private const char CHAR_CIRCUM = '^';

		/// <summary>
		/// 文章に自動的に改行コードを入れるか
		/// </summary>
		private const bool AutoSpace = true;
		/// <summary>
		/// 禁則文字
		/// </summary>
		private static HashSet<char> Hyphnation;
		/// <summary>
		/// スタティックコンストラクタ
		/// </summary>
		static Program()
		{
			Hyphnation = new HashSet<char>();
			Hyphnation.Add('ぁ');
			Hyphnation.Add('ぃ');
			Hyphnation.Add('ぅ');
			Hyphnation.Add('ぇ');
			Hyphnation.Add('ぉ');
			Hyphnation.Add('っ');
			Hyphnation.Add('ゃ');
			Hyphnation.Add('ゅ');
			Hyphnation.Add('ょ');
			Hyphnation.Add('ァ');
			Hyphnation.Add('ィ');
			Hyphnation.Add('ゥ');
			Hyphnation.Add('ェ');
			Hyphnation.Add('ォ');
			Hyphnation.Add('ッ');
			Hyphnation.Add('ャ');
			Hyphnation.Add('ュ');
			Hyphnation.Add('ョ');
			Hyphnation.Add('ー');
			Hyphnation.Add('、');
			Hyphnation.Add('。');
			Hyphnation.Add('”');
			Hyphnation.Add('’');
			Hyphnation.Add('「');
			Hyphnation.Add('」');
			Hyphnation.Add('（');
			Hyphnation.Add('）');
			Hyphnation.Add('《');
			Hyphnation.Add('》');
		}

		/// <summary>
		/// Application Entry Point.
		/// </summary>
		/// <param name="args">パラメータ</param>
		public static void Main(string[] args)
		{
			string ifname = args[0];
			string ofname = args[1];
			string chfname = args[2];

			Program program = new Program();
			program.Run(ifname, ofname, chfname);
		}

		/// <summary>
		/// テキストファイルを .lng ファイルに Pack します
		/// </summary>
		/// <param name="ifname">入力テキストファイル名</param>
		/// <param name="ofname">出力 .lng ファイル名</param>
		/// <param name="chfname">キャラクタテーブルリスト</param>
		private void Run(string ifname, string ofname, string chfname)
		{
			// 入力ファイルを読み込んでテキストに分解
			var texts = this.ReadText(ifname);

			// キャラクタテーブル準備
			var chtable = new List<char>();
			var chmap = new Dictionary<char, int>();
			this.InitializeTable(chtable, chmap);

			/*
			 * テキストデータをメモリに書き出す
			 */
			var textdata = new MemoryStream();
			{
				var writer = new BinaryWriter(textdata);
				foreach (var text in texts)
				{
					string key = text.Item1;
					string value = text.Item2;

					// キー出力
					writer.Write(Encoding.ASCII.GetBytes(key));
					writer.Write((byte)0x00);
					// テキスト出力
					foreach (var ch in value)
					{
						int index = 0;
						// キャラクタマップを確認
						if (chmap.ContainsKey(ch))
						{
							// 既にキャラクタテーブルにある文字
							index = chmap[ch];
						}
						else
						{
							// キャラクタテーブルに存在しない文字
							index = chtable.Count;
							chmap.Add(ch, index);
							chtable.Add(ch);
						}

						// テキスト変換
						if (index > 223)
						{
							int page = (index - 223) / 255;
							int offset = index - 223 - (page * 255);
							if (offset == 0)
							{	
								page -= 1;
								offset = 255;
							}
							writer.Write((byte)(0xE0 | (page & 0x0F)));
							writer.Write((byte)(offset & 0xFF));
						}
						else
						{
							writer.Write((byte)(index & 0xFF));
						}
					}
					writer.Write((byte)0x00);
				}
			}

			/*
			 * .lng file 出力
			 */
			using(var writer = new BinaryWriter(new FileStream(ofname, FileMode.Create, FileAccess.Write)))
			{
				// Header 情報
				writer.Write((UInt64)0x0000000400000000);	// Magic
				writer.Write((UInt64)0x0000000100000000);	// Unknown1

				// キャラクタテーブル
				writer.Write((UInt32)chtable.Count * 2);	// キャラクタテーブルサイズ
				foreach (var ch in chtable)
				{
					writer.Write((UInt16)ch);
				}

				writer.Write((UInt32)0x00000002);			// Unknown2

				// テキストテーブル
				writer.Write((UInt32)textdata.Length);
				writer.Write(textdata.GetBuffer(), 0, (int)textdata.Length);
			}

			/*
			 * Character Table 出力
			 */
			using (var writer = new StreamWriter(chfname, false, Encoding.UTF8))
			{
				int index = 0;
				foreach(var ch in chtable)
				{
					writer.WriteLine(ch);
					index++;
				}
			}
		}

		/// <summary>
		/// 入力テキストファイルを読み込みます
		/// </summary>
		/// <param name="fname">入力元ファイル</param>
		/// <returns>テキストデータ</returns>
		private List<Tuple<string, string>> ReadText(string fname)
		{
			var texts = new List<Tuple<string, string>>();
			using (var reader = new StreamReader(fname, Encoding.UTF8))
			{
				//Regex pattern = new Regex("(?<key>.+?)=(?<value>.+)");
                Regex pattern = new Regex("(?<key>.+?)=(?<value>.*)");//reduxではvalueが空白の場合がある
                while (!reader.EndOfStream)
				{
					var line = reader.ReadLine();
					if(!string.IsNullOrWhiteSpace(line))
					{
						if (!line.StartsWith("#"))	// # から始まる行はコメント
						{
							var match = pattern.Match(line);
							string key = match.Groups["key"].Value;
							string value = match.Groups["value"].Value;
							value = (AutoSpace) ? this.ParseTextValue(value) : value;

							texts.Add(new Tuple<string, string>(key, value));
						}
					}
				}
			}

			return texts;
		}

		/// <summary>
		/// キャラクタテーブルの初期セットアップを行います
		/// </summary>
		/// <param name="chlist">キャラクタリスト</param>
		/// <param name="chmap">キャラクタ辞書</param>
		private void InitializeTable(List<char> chlist, Dictionary<char, int> chmap)
		{
			// stable_us.lng のオリジナルのテーブルを再現
			// Menu などの文字化け対策 -> 意味が無かった
			char[] table = {
                (char)0xF8FF,
                (char)0x0000,
                (char)0x0020,
                (char)0x0065,
                (char)0x0074,
                (char)0x006F,
                (char)0x0061,
                (char)0x006E,
                (char)0x0069,
                (char)0x0072,
                (char)0x0073,
                (char)0x0068,
                (char)0x006C,
                (char)0x0075,
                (char)0x002E,
                (char)0x0064,
                (char)0x006D,
                (char)0x0079,
                (char)0x0063,
                (char)0x0067,
                (char)0x0077,
                (char)0x0066,
                (char)0x0021,
                (char)0x002C,
                (char)0x0070,
                (char)0x006B,
                (char)0x0062,
                (char)0x0076,
                (char)0x0027,
                (char)0x0049,
                (char)0x0041,
                (char)0x0054,
                (char)0x0053,
                (char)0x0045,
                (char)0x0048,
                (char)0x0057,
                (char)0x003F,
                (char)0x004F,
                (char)0x0044,
                (char)0x0052,
                (char)0x004D,
                (char)0x0043,
                (char)0x004C,
                (char)0x004E,
                (char)0x0059,
                (char)0x0050,
                (char)0x002D,
                (char)0x0047,
                (char)0x0042,
                (char)0x0046,
                (char)0x004B,
                (char)0x0055,
                (char)0x006A,
                (char)0x0056,
                (char)0x0078,
                (char)0x003C,
                (char)0x003E,
                (char)0x2026,
                (char)0x2013,
                (char)0x0030,
                (char)0x007A,
                (char)0x0022,
                (char)0x0029,
                (char)0x0028,
                (char)0x0031,
                (char)0x004A,
                (char)0x0071,
                (char)0x005F,
                (char)0x0032,
                (char)0x003A,
                (char)0x0033,
                (char)0x0035,
                (char)0x003D,
                (char)0x0036,
                (char)0x0034,
                (char)0x2019,
                (char)0x0051,
                (char)0x0037,
                (char)0x0038,
                (char)0x0058,
                (char)0x0039,
                (char)0x005A,
                (char)0x002A,
                (char)0x002B,
                (char)0x002F,
                (char)0x0026,
                (char)0x201D,
                (char)0x201C,
                (char)0x003B,
                (char)0x00FC,
                (char)0x005D,
                (char)0x005B,
                (char)0x005C,
                (char)0x00A0,
                (char)0x0023,
                (char)0x00A9,
                (char)0x2014,
                (char)0x0024,
                (char)0x007C,
                (char)0x00ED,
                (char)0x00F6,
                (char)0x00AE,
                (char)0x0040,
                (char)0x2018,
                (char)0x0025,
                (char)0x00E9,
                (char)0x2122,
                (char)0x00E1,
                (char)0x201E,
                (char)0x0142,
                (char)0x00F3,
                (char)0x0441,
                (char)0x0421,
                (char)0x0445,
                (char)0x0060,
                (char)0x0410,
                (char)0x3126,
                (char)0x00C0,
                (char)0x00C1,
                (char)0x00C2,
                (char)0x00C3,
                (char)0x00C4,
                (char)0x00C5,
                (char)0x00C6,
                (char)0x00C7,
                (char)0x00C8,
                (char)0x00C9,
                (char)0x00CA,
                (char)0x00CB,
                (char)0x00CC,
                (char)0x00CD,
                (char)0x00CE,
                (char)0x00CF,
                (char)0x00D0,
                (char)0x00D1,
                (char)0x00D2,
                (char)0x00D3,
                (char)0x00D4,
                (char)0x00D5,
                (char)0x00D6,
                (char)0x00D7,
                (char)0x00D8,
                (char)0x00D9,
                (char)0x00DA,
                (char)0x00DB,
                (char)0x00DC,
                (char)0x00DD,
                (char)0x00DE,
                (char)0x00DF,
                (char)0x00E0,
                (char)0x00E2,
                (char)0x00E3,
                (char)0x00E4,
                (char)0x00E5,
                (char)0x00E6,
                (char)0x00E7,
                (char)0x00E8,
                (char)0x00EA,
                (char)0x00EB,
                (char)0x00EC,
                (char)0x00EE,
                (char)0x00EF,
                (char)0x00F0,
                (char)0x00F1,
                (char)0x00F2,
                (char)0x00F4,
                (char)0x00F5,
                (char)0x00F7,
                (char)0x00F8,
                (char)0x00F9,
                (char)0x00FA,
                (char)0x00FB,
                (char)0x00FD,
                (char)0x00FE,
                (char)0x0001,
                (char)0x0002,
                (char)0x0003,
                (char)0x0004,
                (char)0x0005,
                (char)0x0006,
                (char)0x0007,
                (char)0x0008,
                (char)0x0009,
                (char)0x000A,
                (char)0x000B,
                (char)0x000C,
                (char)0x000D,
                (char)0x000E,
                (char)0x000F,
                (char)0x0010,
                (char)0x0011,
                (char)0x0012,
                (char)0x0013,
                (char)0x0014,
                (char)0x0015,
                (char)0x0016,
                (char)0x0017,
                (char)0x0018,
                (char)0x0019,
                (char)0x001A,
                (char)0x001B,
                (char)0x001C,
                (char)0x001D,
                (char)0x001E,
                (char)0x001F,
                (char)0x005E,
                (char)0x007B,
                (char)0x007D,
                (char)0x007E
            };
			foreach (var ch in table)
			{
				if (chmap.ContainsKey(ch))
				{
					Console.WriteLine("Already exist key - {0}", ch);
					Environment.Exit(-1);
				}
				chmap.Add(ch, chlist.Count);
				chlist.Add(ch);
			}
		}

		/// <summary>
		/// 文章に改行用のコードを追加します
		/// </summary>
		/// <param name="value">テキスト</param>
		/// <returns>改行コード追加済みテキスト</returns>
		private string ParseTextValue(string value)
		{
			var sb = new StringBuilder();
			var chvalue = value.ToCharArray();
			for (int idx = 0; idx < chvalue.Length - 1; idx++)
			{
				var ch = chvalue[idx];
				sb.Append(ch);
				if (ch == CHAR_SPACE)
				{
					// SPACE が改行コントロール用になるので代替としてサーカムフレックスを使う
					sb.Append(CHAR_CIRCUM);
				}
				else if (ch.IsFullWidth())
				{
					var chnext = chvalue[idx + 1];
					if (!Hyphnation.Contains(chnext))
					{
						// 日本語文章中で改行を許可するために SPACE を挿入
						sb.Append(CHAR_SPACE);
					}
				}
			}
			// 最後の一文字を追加
			if (chvalue.Length > 0)
			{
				sb.Append(chvalue[chvalue.Length - 1]);
			}

			return sb.ToString();
		}

	}
}
