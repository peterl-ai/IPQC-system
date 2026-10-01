<template>
  <div class="page-heading">
    <div>
      <p class="page-eyebrow">{{ eyebrow }}</p>
      <h1>{{ title }}</h1>
      <p>{{ subtitle }}</p>
    </div>
    <div class="page-heading-extra"><slot /></div>
  </div>
</template>

<script setup lang="ts">
defineProps<{ title: string; subtitle: string; eyebrow: string }>()
</script>
