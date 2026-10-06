using System.Windows;
using System.Windows.Controls;
using AideDeCamp.Services;

namespace AideDeCamp;
public sealed class ProgressionWindow:Window
{
    public NationProgression.Plan? Plan {get;private set;}
    public ProgressionWindow(NationProgression progression,int side,int id,bool project) {
        Title=project?"Complete project levels":"Complete policy";Width=720;Height=430;MinWidth=520;MinHeight=330;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"SurfaceBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var panel=new StackPanel{Margin=new Thickness(18)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var name=project?progression.Projects.Single(p=>p.Id==id).Name:progression.Policies.Single(p=>p.Id==id).Name;
        panel.Children.Add(new TextBlock{Text=name,FontWeight=FontWeights.Bold,FontSize=18,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text=project?progression.Projects.Single(p=>p.Id==id).Description:progression.Policies.Single(p=>p.Id==id).Description,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});
        var value=new TextBox{Text=(project?progression.Level(side,id)+1:1).ToString(),Width=130,HorizontalAlignment=HorizontalAlignment.Left};
        if(project){panel.Children.Add(new TextBlock{Text="Target completed level"});panel.Children.Add(value);}
        var description=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,14)};panel.Children.Add(description);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        buttons.Children.Add(new Button{Content="Cancel",IsCancel=true});var stage=new Button{Content="Stage completion"};buttons.Children.Add(stage);
        void Preview(){try{Plan=project?progression.CompleteProject(side,id,int.Parse(value.Text)):progression.CompletePolicy(side,id,true);description.Text=Plan.Description;stage.IsEnabled=true;}catch(Exception ex){Plan=null;description.Text=ex.Message;stage.IsEnabled=false;}}
        value.TextChanged+=(_,_)=>Preview();stage.Click+=(_,_)=>{Preview();if(Plan is not null)DialogResult=true;};Preview();
    }
}
