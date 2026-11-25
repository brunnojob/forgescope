namespace ForgeScope.Hub;

public static class HubInteraction
{
    public const string Script = """
    'use strict';
    document.querySelectorAll('[data-file-input]').forEach(input => {
      const status = document.getElementById(input.dataset.fileStatus);
      input.addEventListener('change', () => { status.textContent = input.files?.[0]?.name || status.dataset.empty; });
    });
    document.querySelectorAll('form[data-scan]').forEach(form => form.addEventListener('submit', () => {
      const button = form.querySelector('button[type="submit"],button');
      if (button) { button.disabled = true; button.textContent = form.dataset.busy; }
      form.setAttribute('aria-busy','true');
    }));
    document.querySelectorAll('[data-retry]').forEach(el => {
      let seconds = Number(el.dataset.retry) || 60;
      el.textContent = seconds;
      const timer = setInterval(() => { seconds = Math.max(0,seconds-1); el.textContent=seconds; if(!seconds) clearInterval(timer); },1000);
    });
    const dialog = document.getElementById('guided-tour');
    if (dialog) {
      const steps = Array.from(dialog.querySelectorAll('[data-tour-step]'));
      const progress = dialog.querySelector('progress');
      const counter = dialog.querySelector('[data-tour-counter]');
      const back = dialog.querySelector('[data-tour-back]');
      const next = dialog.querySelector('[data-tour-next]');
      let index = 0, origin;
      const clear = () => document.querySelectorAll('.tour-highlight').forEach(el=>el.classList.remove('tour-highlight'));
      const show = () => {
        clear(); steps.forEach((step,i)=>step.hidden=i!==index);
        progress.value=index+1; progress.max=steps.length;
        counter.textContent=`${index+1} / ${steps.length}`;
        back.disabled=index===0; next.textContent=index===steps.length-1?next.dataset.finish:next.dataset.next;
        const target = steps[index].dataset.target;
        if(target) { const el=document.querySelector(target); if(el){el.classList.add('tour-highlight');el.scrollIntoView({block:'center',behavior:'auto'});} }
        dialog.querySelector('[data-tour-focus]').focus();
      };
      document.querySelectorAll('[data-tour-start]').forEach(button=>button.addEventListener('click',()=>{origin=button;index=0;dialog.showModal();show();}));
      back.addEventListener('click',()=>{if(index>0){index--;show();}});
      next.addEventListener('click',()=>{if(index<steps.length-1){index++;show();}else dialog.close();});
      dialog.querySelector('[data-tour-close]').addEventListener('click',()=>dialog.close());
      dialog.addEventListener('close',()=>{clear();origin?.focus();});
      dialog.addEventListener('keydown',event=>{
        if(event.target.matches('input,textarea')) return;
        if(event.key==='ArrowRight'){event.preventDefault();next.click();}
        if(event.key==='ArrowLeft'&&index>0){event.preventDefault();back.click();}
      });
      dialog.querySelectorAll('[data-demo-choice]').forEach(button=>button.addEventListener('click',()=>{
        dialog.querySelectorAll('[data-demo-choice]').forEach(b=>b.setAttribute('aria-pressed',String(b===button)));
        dialog.querySelectorAll('[data-demo-result]').forEach(result=>result.hidden=result.dataset.demoResult!==button.dataset.demoChoice);
      }));
    }
    """;
}
