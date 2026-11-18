using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;
using ScreenCoverSupport;

namespace ScreenCoverUpgrade {
 public static class Management {
  public static void Open(){LocalStore.Ensure();Application.EnableVisualStyles();using(var login=new Form()){
   bool first=!File.Exists(LocalStore.At("legacy-verifier.dat"));
   login.Text=first?"首次安装 · 设置6字符机主密码":"遮屏锁设置 · 验证机主密码";login.Size=new Size(390,first?210:165);login.StartPosition=FormStartPosition.CenterScreen;
   var text=new TextBox{Left=20,Top=25,Width=330,UseSystemPasswordChar=true,ImeMode=ImeMode.Disable};
   var confirm=new TextBox{Left=20,Top=65,Width=330,UseSystemPasswordChar=true,ImeMode=ImeMode.Disable};
   var ok=new Button{Text=first?"设置密码":"验证",Left=250,Top=first?110:65,Width=100};ok.Click+=(s,e)=>{
    if(first){try{if(text.Text!=confirm.Text)throw new InvalidOperationException("两次输入不一致。");PasswordVault.CreateInitial(text.Text);text.Clear();confirm.Clear();login.DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(login,ex.Message);}return;}
    if(PasswordVault.VerifyLegacy(text.Text)){text.Clear();login.DialogResult=DialogResult.OK;}else{MessageBox.Show(login,"密码不正确。");text.Clear();}};
   if(first)login.Controls.Add(confirm);
   login.Controls.Add(text);login.Controls.Add(ok);login.AcceptButton=ok;
   if(login.ShowDialog()!=DialogResult.OK)return;
  }Application.Run(new SettingsForm());}
 }
 public sealed class SettingsForm:Form {
  Settings settings=Settings.Load();FlowLayoutPanel list;ComboBox cameras,inputPos;CheckBox recording,warning,showErrorImg,mouse;
  TextBox normal,dark,digitFolder,digitSingle,completeImg,errorImg,folder;NumericUpDown idle,duration,maximum,days,capacity,inputTimeout;Label status;Button test;
  public SettingsForm(){
   Text="遮屏锁 · 设置与记录";Size=new Size(820,860);MinimumSize=new Size(700,580);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
   list=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16)};Controls.Add(list);
   LabelLine("设置在下次启动遮屏时生效。录像默认关闭；所有自定义图片仅保存在本机，可随时恢复默认图形。",42);

   normal=ImageRow("正常背景图",settings.NormalImage,"cover-background.png",false);
   dark=ImageRow("暗图（留空自动变暗）",settings.DarkImage,"cover-dark.png",false);
   digitFolder=PathRow("输入图标目录(1~5)",settings.DigitIconFolder,true);
   digitSingle=ImageRow("单张输入图标(备选)",settings.DigitImage,"digit-1.png",false);
   completeImg=ImageRow("输满6位图标",settings.CompleteImage,"digit-complete.png",false);
   errorImg=ImageRow("错误提示图",settings.ErrorImage,"error-badge.png",false);
   showErrorImg=Check("输入错误时显示错误提示图",settings.ShowErrorImage);

   var posRow=Row("输入指示区位置");
   inputPos=new ComboBox{Width=200,DropDownStyle=ComboBoxStyle.DropDownList};
   inputPos.Items.AddRange(new object[]{"Bottom","Center","Top"});
   inputPos.SelectedItem=settings.InputPosition=="Center"||settings.InputPosition=="Top"?settings.InputPosition:"Bottom";
   posRow.Controls.Add(inputPos);

   idle=Number("无本地操作后变暗（秒）",settings.IdleSeconds,1,3600);
   inputTimeout=Number("输入尝试超时清空（秒）",settings.InputTimeoutSeconds,10,3600);
   warning=Check("错误输入时恢复正常图并显示警示文字",settings.WarnOnError);
   recording=Check("开启异常录像（仅本机视频，不录声音）",settings.RecordingEnabled);
   mouse=Check("明确的本地鼠标操作也触发录像（默认关闭）",settings.MouseRecording);
   var camrow=Row("摄像头");cameras=new ComboBox{Width=350,DropDownStyle=ComboBoxStyle.DropDownList};camrow.Controls.Add(cameras);
   if(!string.IsNullOrEmpty(settings.Camera)){cameras.Items.Add(settings.Camera);cameras.SelectedIndex=0;}
   var refresh=new Button{Text="查找",Width=70};camrow.Controls.Add(refresh);refresh.Click+=async(s,e)=>{refresh.Enabled=false;try{var result=await Task.Run(()=>CameraTools.Devices());cameras.Items.Clear();cameras.Items.AddRange(result);if(result.Length>0)cameras.SelectedIndex=0;else MessageBox.Show(this,"未发现可用摄像头。请检查 Windows 摄像头隐私设置与驱动。");}catch(Exception ex){MessageBox.Show(this,LocalStore.Error(ex));}finally{refresh.Enabled=true;}};
   test=new Button{Text="手动测试画面和拍摄范围（约3秒）",Width=430,Height=32};list.Controls.Add(test);test.Click+=Test;
   duration=Number("末次触发后的录像时长（秒）",settings.ClipSeconds,1,600);maximum=Number("单段最长（秒）",settings.MaxClipSeconds,1,3600);
   folder=PathRow("录像目录",settings.SaveDirectory,true);days=Number("保留天数",settings.RetentionDays,1,3650);capacity=Number("容量上限（MB）",settings.CapacityMB,16,1048576);
   var init=new Button{Text="首次升级：验证原密码并启用逐位检测",Width=430,Height=32};init.Click+=(s,e)=>InitializePassword();list.Controls.Add(init);
   var events=new Button{Text="查看 / 播放 / 删除事件与录像",Width=430,Height=32};events.Click+=(s,e)=>{using(var page=new EventsForm())page.ShowDialog(this);};list.Controls.Add(events);
   var save=new Button{Text="保存设置",Width=160,Height=36};list.Controls.Add(save);save.Click+=(s,e)=>{try{
    if((int)maximum.Value<(int)duration.Value)throw new InvalidOperationException("单段上限不能小于录像时长。");
    string camera=cameras.SelectedItem as string??"";
    if(recording.Checked&&(settings.TestedCamera!=camera||string.IsNullOrEmpty(camera)))throw new InvalidOperationException("请先选择摄像头并完成手动测试，再开启录像。");
    settings.NormalImage=normal.Text.Trim();settings.DarkImage=dark.Text.Trim();
    settings.DigitIconFolder=digitFolder.Text.Trim();settings.DigitImage=digitSingle.Text.Trim();
    settings.CompleteImage=completeImg.Text.Trim();settings.ErrorImage=errorImg.Text.Trim();
    settings.ShowErrorImage=showErrorImg.Checked;
    settings.InputPosition=(inputPos.SelectedItem as string)??"Bottom";
    settings.IdleSeconds=(int)idle.Value;settings.InputTimeoutSeconds=(int)inputTimeout.Value;
    settings.WarnOnError=warning.Checked;settings.RecordingEnabled=recording.Checked;settings.MouseRecording=mouse.Checked;settings.Camera=camera;
    settings.ClipSeconds=(int)duration.Value;settings.MaxClipSeconds=(int)maximum.Value;settings.RetentionDays=(int)days.Value;settings.CapacityMB=(int)capacity.Value;settings.SaveDirectory=folder.Text;
    settings.Save();status.Text="已保存。将在下次启动遮屏时生效。";
   }catch(Exception ex){MessageBox.Show(this,LocalStore.Error(ex));}};
   status=LabelLine(PasswordVault.Load()==null?"逐位检测尚未初始化：仍用原密码整串验证。":"逐位检测资料已初始化。",45);
  }
  Label LabelLine(string text,int height){var l=new Label{Text=text,Width=720,Height=height};list.Controls.Add(l);return l;}
  FlowLayoutPanel Row(string title){var row=new FlowLayoutPanel{Width=740,Height=36};row.Controls.Add(new Label{Text=title,Width=200,Height=28,TextAlign=ContentAlignment.MiddleLeft});list.Controls.Add(row);return row;}
  CheckBox Check(string text,bool value){var c=new CheckBox{Text=text,Checked=value,Width=680,Height=32};list.Controls.Add(c);return c;}
  NumericUpDown Number(string title,int value,int min,int max){var row=Row(title);var n=new NumericUpDown{Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),Width=140};row.Controls.Add(n);return n;}
  TextBox PathRow(string title,string value,bool directory){var row=Row(title);var t=new TextBox{Text=value,Width=310};row.Controls.Add(t);var b=new Button{Text="选择",Width=65};row.Controls.Add(b);b.Click+=(s,e)=>{
   if(directory){using(var dialog=new FolderBrowserDialog())if(dialog.ShowDialog(this)==DialogResult.OK)t.Text=dialog.SelectedPath;}
   else{using(var dialog=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp"})if(dialog.ShowDialog(this)==DialogResult.OK)t.Text=dialog.FileName;}
  };var clr=new Button{Text="清除",Width=65};row.Controls.Add(clr);clr.Click+=(s,e)=>{t.Text="";};return t;}
  TextBox ImageRow(string title,string value,string fallbackResource,bool directory){
   var t=PathRow(title,value,directory);
   var preview=new Button{Text="预览"+title,Width=180,Height=28};list.Controls.Add(preview);
   preview.Click+=(s,e)=>{
    try{
     Image image=null;
     if(string.IsNullOrWhiteSpace(t.Text)&&fallbackResource=="cover-dark.png"&&!string.IsNullOrWhiteSpace(normal.Text)){
      using(var baseImg=CoverBackground.LoadFromFileOrResource(normal.Text,"cover-background.png"))
       image=CoverBackground.DimCopy(baseImg,0.28f);
     }else{
      image=CoverBackground.LoadFromFileOrResource(t.Text.Trim(),fallbackResource);
     }
     if(image==null)throw new FileNotFoundException("未能加载图片，请检查路径。");
     using(image)using(var f=new Form{Text=title,Size=new Size(800,550),StartPosition=FormStartPosition.CenterParent}){
      f.Controls.Add(new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Image=image});
      f.ShowDialog(this);
     }
    }catch(Exception ex){MessageBox.Show(this,LocalStore.Error(ex));}
   };
   return t;
  }
  void InitializePassword(){using(var f=new Form{Text="只在本机验证原密码",Size=new Size(420,200),StartPosition=FormStartPosition.CenterParent}){
   var note=new Label{Text="密码不会保存为明文。验证后建立当前 Windows 用户保护的前缀校验资料。",Left=15,Top=10,Width=375,Height=40};
   var box=new TextBox{Left=15,Top=60,Width=375,UseSystemPasswordChar=true,ImeMode=ImeMode.Disable};
   var b=new Button{Text="验证并初始化",Left=235,Top=100,Width=155};b.Click+=(s,e)=>{try{PasswordVault.Initialize(box.Text);box.Clear();status.Text="已完成逐位检测初始化，下次启动生效。";f.Close();}catch(Exception ex){box.Clear();MessageBox.Show(f,ex.Message);}};
   f.Controls.AddRange(new Control[]{note,box,b});f.AcceptButton=b;f.ShowDialog(this);
  }}
  async void Test(object sender,EventArgs args){
   string camera=cameras.SelectedItem as string;if(string.IsNullOrEmpty(camera)){MessageBox.Show(this,"请先查找并选择摄像头。");return;}
   test.Enabled=false;string id=LocalStore.Id();
   try {
    // This explicit user action is the only initial camera test path.
    settings.Camera=camera;settings.RecordingEnabled=false;settings.Save();recording.Checked=false;
    Directory.CreateDirectory(LocalStore.At("Events"));RecorderClient.Send("test",id,"manual_test");status.Text="正在获取测试画面，请留意 Windows 权限提示和摄像头指示灯。";
    Clip result=await Task.Run(()=>{for(int n=0;n<400;n++){foreach(string file in Directory.GetFiles(LocalStore.At("Events"),"*.json")){var c=LocalStore.Read<Clip>(file);if(c.Events.Any(e=>e.Attempt==id)&&c.CompletedUtc!=null){if(c.Status=="saved"&&!File.Exists(c.Path+".jpg")){System.Threading.Thread.Sleep(150);continue;}return c;}}System.Threading.Thread.Sleep(100);}throw new TimeoutException("摄像头测试超时，请到事件记录查看失败详情。");});
    if(result.Status!="saved")throw new InvalidOperationException(result.Status+": "+result.Failure);
    using(var f=new Form{Text="摄像头测试 · 请确认拍摄范围",Size=new Size(800,600),StartPosition=FormStartPosition.CenterParent})using(var img=Image.FromFile(result.Path+".jpg")){
     var yes=new Button{Text="画面正常且拍摄范围合适",Dock=DockStyle.Bottom,Height=42,DialogResult=DialogResult.OK};f.Controls.Add(new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Image=img});f.Controls.Add(yes);
     if(f.ShowDialog(this)==DialogResult.OK){settings.TestedCamera=camera;settings.Save();status.Text="测试通过。可以主动勾选开启录像，再保存设置。";}
    }
   }catch(Exception ex){MessageBox.Show(this,LocalStore.Error(ex));status.Text="测试失败；录像保持关闭。";}finally{test.Enabled=true;}
  }
 }
 public sealed class EventsForm:Form {
  DataGridView grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AutoGenerateColumns=true,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};List<Clip> clips;
  public EventsForm(){Text="本机事件记录";Size=new Size(1050,620);Controls.Add(grid);var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=45};Controls.Add(bar);
   Button b=new Button{Text="刷新"};bar.Controls.Add(b);b.Click+=(s,e)=>RefreshRows();
   b=new Button{Text="播放录像"};bar.Controls.Add(b);b.Click+=(s,e)=>{var c=Selected();if(c!=null&&c.CompletedUtc!=null&&c.Path!=null&&File.Exists(c.Path))Process.Start(new ProcessStartInfo(c.Path){UseShellExecute=true});};
   b=new Button{Text="打开目录",Width=100};bar.Controls.Add(b);b.Click+=(s,e)=>{var c=Selected();string path=c!=null&&c.Path!=null?Path.GetDirectoryName(c.Path):Settings.Load().SaveDirectory;Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo("explorer.exe",CameraTools.Quote(path)){UseShellExecute=true});};
   b=new Button{Text="删除选中",Width=100};bar.Controls.Add(b);b.Click+=(s,e)=>{var c=Selected();if(c!=null&&c.CompletedUtc!=null&&MessageBox.Show(this,"删除这条记录及对应录像？","确认",MessageBoxButtons.YesNo)==DialogResult.Yes){RecorderClient.SafeSend("delete",c.Id,"");}};
   b=new Button{Text="清空已完成记录",Width=145};bar.Controls.Add(b);b.Click+=(s,e)=>{if(MessageBox.Show(this,"删除全部已完成记录和录像？正在写入的片段会保留。","确认",MessageBoxButtons.YesNo)==DialogResult.Yes)RecorderClient.SafeSend("clear",null,"");};
   RefreshRows();
  }
  Clip Selected(){return grid.CurrentRow==null?null:clips[grid.CurrentRow.Index];}
  void RefreshRows(){try{Directory.CreateDirectory(LocalStore.At("Events"));clips=Directory.GetFiles(LocalStore.At("Events"),"*.json").Select(f=>LocalStore.Read<Clip>(f)).OrderByDescending(c=>c.CreatedUtc).ToList();
   grid.DataSource=clips.Select(c=>new{时间=c.CreatedUtc,触发原因=string.Join("；",c.Events.Select(e=>(e.Reason=="password_error"?"密码错误":e.Reason=="emergency"?"应急解除":e.Reason=="mouse"?"本地鼠标":e.Reason)+" "+e.Utc)),状态=c.Status,失败详情=c.Failure,文件=c.Path}).ToList();
  }catch(Exception e){MessageBox.Show(this,LocalStore.Error(e));}}
 }
}
