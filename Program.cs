using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ResizeTo3MB
{
	class Program
	{
		static void Main(string[] args)
		{
			List<string> lst = new List<string>();

			if (args.Length > 0)
			{
				lst.AddRange(args.Where<string>(s => s.EndsWith(".jpg") | s.EndsWith(".png") | s.EndsWith(".bmp")));
			}
			else
			{
				lst.AddRange(Directory.GetFiles("./", "*.jpg", SearchOption.TopDirectoryOnly));
				lst.AddRange(Directory.GetFiles("./", "*.png", SearchOption.TopDirectoryOnly));
				lst.AddRange(Directory.GetFiles("./", "*.bmp", SearchOption.TopDirectoryOnly));
			}

			foreach (string s in lst)
				ResizeImage(s);

// 			Parallel.ForEach<string>(
// 				lst,
// 				(s) =>
// 				{
// 					try
// 					{
// 						ResizeImage(s);
// 					}
// 					catch
// 					{ }
// 				});
		}

		private const int MaxSize = 2883584;	// 약 2.75 MB

		class Data
		{
			public Image	Image;
			public byte[]	Bytes;

			public Size		SOri;
			public Size		SRes;

			public long		COri;
			public long		CRes;

			public string	Path;
		}

		private static void ResizeImage(string path)
		{
			if (new FileInfo(path).Length < MaxSize) return;

			Data data = new Data();

			data.Image	= Image.FromFile(path);
			data.Path	= path;
			data.Bytes	= File.ReadAllBytes(path);

			data.SOri	= data.Image.Size;
			data.COri	= data.Bytes.Length;

			// not available GIF now.
			ImageCodecInfo		codec;
			EncoderParameters	param;

			if (data.Image.RawFormat.Guid == ImageFormat.Jpeg.Guid || !IsImageTransparent(data.Image))
			{
				codec = ImageCodecInfo.GetImageDecoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
				param = new EncoderParameters(1);

				// PropertyTagJPEGQuality 를 찾는다. 없으면 100퍼
				long quality = 100;

				if (data.Image.PropertyIdList.Any(e => e == 0x5010))
					quality = data.Image.PropertyItems.First(e => e.Id == 0x5010).Value[0];

				param.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

				ResizeJpg(data, codec, param);
			}
			else
			{
				codec = ImageCodecInfo.GetImageDecoders().First(e => e.FormatID == ImageFormat.Png.Guid);
				param = new EncoderParameters(1);
				param.Param[0] = new EncoderParameter(Encoder.ColorDepth, Bitmap.GetPixelFormatSize(data.Image.PixelFormat));

				ResizePng(data, codec, param);
			}

			data.Image.Dispose();

			Console.WriteLine(
				"{0}x{1} {2} => {3}x{4} {5} : {6}",
				data.SOri.Width, data.SOri.Height,
				ToSize(data.COri),
				data.SRes.Width, data.SRes.Height,
				ToSize(data.CRes),
				Path.GetFileName(path));

			if (!Directory.Exists("Backup")) Directory.CreateDirectory("Backup");

			File.Move(path, Path.Combine(Path.Combine(Path.GetDirectoryName(path), "Backup"), Path.GetFileName(path)));

			File.WriteAllBytes(path, data.Bytes);
		}

		// 픽셀 수 계산
		//						JPG		PNG
		// Compression ratio	(va)	50%
		// Bytes per pixel		2.1		(va)
		private static void ResizeJpg(Data data, ImageCodecInfo codec, EncoderParameters param)
		{
			// 품빌 변경 : 80
			// 품질 변경 : 60
			// 사이즈 변경
			// 0.9 배씩 줄이면서 변경
			int origQuaility = param.Param[0].NumberOfValues;
			int quaility = param.Param[0].NumberOfValues;
			quaility = quaility - (quaility % 10);

			int w = data.Image.Width;
			int h = data.Image.Height;

			do
			{
				ResizeBySize(data, codec, param, w, h);

				w = (int)(w * 0.9f);
				h = (int)(h * 0.9f);
			}
			while (data.Bytes.Length > MaxSize);
		}

		private static void ResizePng(Data data, ImageCodecInfo codec, EncoderParameters param)
		{
			int w, h;

			GetSizeFromPixels(MaxSize * param.Param[0].NumberOfValues / 8 * 2, data.Image.Width, data.Image.Height, out w, out h);

			do
			{
				ResizeBySize(data, codec, param, w, h);

				w = (int)(w * 0.9f);
				h = (int)(h * 0.9f);
			}
			while (data.Bytes.Length > MaxSize);
		}
		
		private static void GetSizeFromPixels(int pixels, int oriW, int oriH, out int newW, out int newH)
		{
			newW = (int)Math.Ceiling(Math.Sqrt(pixels * oriW / oriH));
			newH = (int)Math.Ceiling(Math.Sqrt(pixels * oriH / oriW));

			if (newW > oriW) newW = oriW;
			if (newH > oriH) newH = oriH;
		}

		private static void ResizeBySize(Data data, ImageCodecInfo codec, EncoderParameters param, int w, int h)
		{
			using (Image imageNew = new Bitmap(w, h, data.Image.PixelFormat))
			{
				using (Graphics g = Graphics.FromImage(imageNew))
				{
					foreach (PropertyItem propertyItem in data.Image.PropertyItems)
						imageNew.SetPropertyItem(propertyItem);

					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.PixelOffsetMode = PixelOffsetMode.HighQuality;
					g.SmoothingMode = SmoothingMode.AntiAlias;

					g.DrawImage(data.Image, 0, 0, w, h);
				}

				using (MemoryStream memStream = new MemoryStream())
				{
					imageNew.Save(memStream, codec, param);
					data.Bytes = memStream.ToArray();

					data.SRes = imageNew.Size;
					data.CRes = data.Bytes.Length;
				}
			}
		}

		private static bool IsImageTransparent(Image image)
		{
			PixelFormat[] formatsWithAlpha =
			{
				PixelFormat.Indexed,				PixelFormat.Gdi,				PixelFormat.Alpha,
				PixelFormat.PAlpha,					PixelFormat.Canonical,			PixelFormat.Format1bppIndexed,
				PixelFormat.Format4bppIndexed,		PixelFormat.Format8bppIndexed,	PixelFormat.Format16bppArgb1555,
				PixelFormat.Format32bppArgb,		PixelFormat.Format32bppPArgb,	PixelFormat.Format64bppArgb,
				PixelFormat.Format64bppPArgb
			};

			bool isTransparent = false;

			if (formatsWithAlpha.Contains(image.PixelFormat))
			{
				Bitmap bitmap = image as Bitmap;
				BitmapData binaryImage = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format64bppArgb);

				unsafe
				{
					byte* pointerToImageData = (byte*)binaryImage.Scan0;
					int numberOfPixels = bitmap.Width * bitmap.Height;

					isTransparent = false;

					// 8 bytes = 64 bits, since our image is 64bppArgb.
					for (int i = 0; i < numberOfPixels * 8; i += 8)
					{
						// Check the last two bytes (transparency channel). First six bytes are for R, G and B channels. (0, 32) means 100% opacity.
						if (pointerToImageData[i + 6] != 0 || pointerToImageData[i + 7] != 32)
						{
							isTransparent = true;
							break;
						}
					}
				}

				bitmap.UnlockBits(binaryImage);
			}

			return isTransparent;
		}

		private static string ToSize(long size)
		{
			if (size < 1000)
				return string.Format("{0:##0.0} B", size);
			else if (size < 1024000)
				return string.Format("{0:##0.0} KiB", size / 1024d);
			else if (size < 1048576000)
				return string.Format("{0:##0.0} MiB", size / 1048576d);
			else
				return string.Format("{0:##0.0} GiB", size / 1073741824d);

		}
	}
}
