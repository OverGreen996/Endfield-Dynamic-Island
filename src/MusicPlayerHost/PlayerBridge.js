if (!window.islandMusic) {
  const controller = { repeat:0, shuffle:null, baseline:null, restoredPlayer:null, restoredVideo:'' };
  const player = () => document.getElementById('movie_player');
  const video = () => document.querySelector('video');
  document.addEventListener('ended',event=>{
    if(event.target?.tagName!=='VIDEO'||controller.repeat!==1||document.querySelector('#movie_player.ad-showing'))return;
    const p=player(),list=p?.getPlaylist?.()||[];
    if(list.length&&p.getPlaylistIndex?.()===list.length-1&&typeof p.playVideoAt==='function'){
      event.stopImmediatePropagation();p.playVideoAt(0);p.playVideo();
    }
  },true);
  controller.snapshot = () => {
    if(location.pathname==='/playlist'){
      const listId=new URL(location.href).searchParams.get('list');
      const link=Array.from(document.querySelectorAll('a[href]')).find(a=>{try{const u=new URL(a.href,location.origin);return u.hostname==='www.youtube.com'&&u.pathname==='/watch'&&u.searchParams.get('list')===listId&&/^[A-Za-z0-9_-]{11}$/.test(u.searchParams.get('v')||'');}catch{return false;}});
      const id=link?new URL(link.href,location.origin).searchParams.get('v'):null;
      return {firstVideo:/^[A-Za-z0-9_-]{11}$/.test(id||'')?id:''};
    }
    const p=player(),v=video(),d=p?.getVideoData?.()||{},m=navigator.mediaSession?.metadata;
    if(p&&v&&!controller.started){controller.started=true;p.playVideo?.();v.play().catch(()=>{});}
    const list=p?.getPlaylist?.()||[];
    if(!controller.baseline&&list.length)controller.baseline=[...list];
    const shuffleButton=document.querySelector('ytd-playlist-panel-renderer button[aria-pressed][aria-label*="隨機"],ytd-playlist-panel-renderer button[aria-pressed][aria-label*="Shuffle"],ytd-playlist-panel-renderer button[aria-pressed][aria-label*="shuffle"]');
    const changed=controller.baseline&&list.some((id,i)=>id!==controller.baseline[i]);
    const ad=!!document.querySelector('#movie_player.ad-showing');
    const ready=!!p&&!!v&&v.readyState>=2;
    if(v && controller.repeat===2 && !ad){v.loop=true;p.setLoopVideo?.(true);}
    const duration=Number.isFinite(v?.duration)?v.duration:0;
    return {ready,title:String(m?.title||d.title||'YouTube 播放清單').slice(0,512),artist:String(m?.artist||d.author||'YouTube').slice(0,512),video:/^[A-Za-z0-9_-]{11}$/.test(d.video_id||'')?d.video_id:'',playing:!!v&&!v.paused,position:Number.isFinite(v?.currentTime)?v.currentTime:0,duration,shuffle:controller.shuffle??(!!changed||shuffleButton?.getAttribute('aria-pressed')==='true'),repeat:controller.repeat,count:Math.min(list.length,10000),error:String(document.querySelector('.ytp-error-content-wrap')?.innerText||'').slice(0,200),ad,canShuffle:typeof p?.setShuffle==='function'&&list.length>1,canRepeat:typeof p?.setLoop==='function'&&typeof p?.setLoopVideo==='function'};
  };
  controller.command = ({kind,value,force=false}) => {
    const p=player(),v=video();if(!p||!v)return false;
    if(kind==='Play'){p.playVideo();return true;}
    if(kind==='Pause'){HTMLMediaElement.prototype.pause.call(v);return v.paused;}
    if(v.readyState<2)return false;
    if(kind==='Previous'){p.previousVideo();return true;}
    if(kind==='Next'){p.nextVideo();return true;}
    if(kind==='Seek'){if(!Number.isFinite(v.duration))return false;p.seekTo(Math.max(0,Math.min(v.duration,value)),true);return true;}
    if(kind==='Shuffle'&&(value===0||value===1)){
      const before=[...(p.getPlaylist?.()||[])];if(before.length<2||typeof p.setShuffle!=='function')return false;
      if(!force&&controller.snapshot().shuffle===(value===1)){controller.shuffle=value===1;return true;}
      p.setShuffle(value===1);const after=p.getPlaylist?.()||[];
      const matched=typeof p.getShuffle==='function'?p.getShuffle()===(value===1):value===1?(before.some((id,i)=>id!==after[i])||after.some((id,i)=>id!==controller.baseline?.[i])):(before.some((id,i)=>id!==after[i])||after.every((id,i)=>id===controller.baseline?.[i]));
      if(matched)controller.shuffle=value===1;
      return matched;
    }
    if(kind==='Repeat'&&[0,1,2].includes(value)&&typeof p.setLoop==='function'&&typeof p.setLoopVideo==='function'){
      p.setLoop(value===1);p.setLoopVideo(value===2);v.loop=value===2;
      if(!!p.getLoopVideo?.()!==(value===2))return false;
      controller.repeat=value;return true;
    }
    return false;
  };
  controller.restore = ({shuffle=null,repeat=null}) => {
    const p=player(),v=video(),id=p?.getVideoData?.().video_id||'';
    if(!p||!v||v.readyState<2||!id||document.querySelector('#movie_player.ad-showing'))return;
    if(controller.restoredPlayer===p&&controller.restoredVideo===id)return;
    // The host owns preferences across complete navigations. Reapply once per
    // ready track/player rather than treating a rebuilt DOM as "shuffle off".
    let applied=true;
    if(typeof shuffle==='boolean')applied=controller.command({kind:'Shuffle',value:shuffle?1:0,force:true})&&applied;
    if([0,1,2].includes(repeat))applied=controller.command({kind:'Repeat',value:repeat})&&applied;
    if(applied){controller.restoredPlayer=p;controller.restoredVideo=id;}
  };
  window.islandMusic=controller;
}
